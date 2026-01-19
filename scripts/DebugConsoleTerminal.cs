namespace GodotDebugConsole;

using Godot;
using System;
using System.Reflection;
using System.Runtime.Loader;
using System.Runtime.CompilerServices;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using SysStrAssemblyDict = System.Collections.Generic.Dictionary<string, System.Reflection.Assembly>;
using SysStrObjectDict = System.Collections.Generic.Dictionary<string, object>;


// custom global variables
// as D in dynamic compiled code
public class ScriptGlobals
{
	public Node SCENE;
	public MultiTypeDict VARS;
}


public class MultiTypeDict
{
	private SysStrObjectDict _data = new SysStrObjectDict();

	public object Set(string name, object value)
	{
		_data[name] = value;
		return value;
	}

	public object Get(string name)
	{
		if (_data.TryGetValue(name, out var val))
			return val;
		GD.PrintErr($"Terminal Error: Variable '%{name}' not found.");
		return null;
	}
}


public class DebugConsoleTerminal
{

	private class ScriptResult
	{
		public object ReturnValue { get; set; }
		public bool IsSuccess { get; set; }
		public string ErrorMessage { get; set; }
	}

	// custom ALC for isCollectible=true
	private class UnloadableScriptContext : AssemblyLoadContext
	{
		private readonly SysStrAssemblyDict _sharedAssemblies;

		public UnloadableScriptContext(SysStrAssemblyDict sharedAssemblies) : base(isCollectible: true)
		{
			_sharedAssemblies = sharedAssemblies;
		}

		protected override Assembly Load(AssemblyName assemblyName)
		{
			if (_sharedAssemblies.TryGetValue(assemblyName.FullName, out var assembly))
			{
				return assembly;
			}
			return null;
		}
	}

	private SysStrAssemblyDict _sharedAssemblies;

	public void InitTerminal()
	{
		// ensure dll loaded
		_ = typeof(Microsoft.CSharp.RuntimeBinder.Binder).TypeHandle;
		_ = typeof(System.Runtime.CompilerServices.DynamicAttribute).TypeHandle;
		_sharedAssemblies = new SysStrAssemblyDict();
		var domainAssemblies = AppDomain.CurrentDomain.GetAssemblies();
		foreach (var assembly in domainAssemblies.Where(a => !a.IsDynamic))
		{
			_sharedAssemblies[assembly.FullName] = assembly;
		}
	}

	private string _RegexCommand(string command)
	{
		// %a = 1 -> D.VARS.Set("a", 1)
		string assignPattern = @"%([A-Za-z0-9_]+)\s*=\s*(.*)";
		if (Regex.IsMatch(command, assignPattern))
		{
			command = Regex.Replace(command, assignPattern, m => {
				string varName = m.Groups[1].Value;
				string value = m.Groups[2].Value;
				return $"D.VARS.Set(\"{varName}\", {value})";
			});
		}

		// %a + %b -> ((dynamic)D.VARS.Get<int>("a")) + ((dynamic)D.VARS.Get<int>("b"))
		string accessPattern = @"(?<!\w)%([A-Za-z0-9_]+)";
		command =  Regex.Replace(command, accessPattern, m => {
			string varName = m.Groups[1].Value;
			return $"((dynamic)D.VARS.Get(\"{varName}\"))";
		});

		// #Player -> ((dynamic)D.SCENE.GetNode("Player"))
		string nodePattern = @"#([A-Za-z0-9_\/]+)";
		command = Regex.Replace(command, nodePattern, "((dynamic)D.SCENE.GetNode(\"$1\"))");

		return command;
	}

	private void _GetRegexCommandLastNodePathAndPostFix(string command, out string nodePath, out string postFix)
	{
		nodePath = "";
		postFix = "";
		var lastMatch = Regex.Matches(command, @"#([A-Za-z0-9_\/]+)").LastOrDefault();
		if (lastMatch != null)
		{
			nodePath = lastMatch.Groups[1].Value;
			int suffixStart = lastMatch.Index + lastMatch.Length;
			postFix = command.Substring(suffixStart).TrimStart('.'); 
		}
	}

	public object EvaluateCommand(string command, ScriptGlobals globals)
	{
		string commandExpanded = _RegexCommand(command);

		var result = _TryCompileAndExecute(commandExpanded, true, globals);
		if (result.IsSuccess)
		{
			return result.ReturnValue;
		}

		var statementResult = _TryCompileAndExecute(commandExpanded, false, globals);
		if (statementResult.IsSuccess)
		{
			return statementResult.ReturnValue;
		}

		return $"Terminal Execution Failed:\n{result.ErrorMessage}";
	}

	private ScriptResult _TryCompileAndExecute(string command, bool isExpression, ScriptGlobals globals)
	{
		var result = new ScriptResult();
		UnloadableScriptContext alc = null;

		var scriptBody = isExpression
			? $@"
				object expressionResult = {command};
				return expressionResult?.ToString() ?? ""null""; 
			" :
			$@"
				{command};
				return ""(void)"";
			";

		var fullScriptCode = $@"
			using System;
			using Godot;
			public class ScriptWrapper
			{{
				public static string RunScript(GodotDebugConsole.ScriptGlobals D)
				{{
					{scriptBody}
				}}
			}}
		";

		var references = _sharedAssemblies.Values.Select(_GetReference).ToList();
		var compilation = CSharpCompilation.Create(
			assemblyName: Guid.NewGuid().ToString() + ".dll",
			syntaxTrees: [CSharpSyntaxTree.ParseText(fullScriptCode)],
			references: references,
			options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

		try
		{
			using var ms = new MemoryStream();
			var emitResult = compilation.Emit(ms);

			if (!emitResult.Success)
			{
				result.IsSuccess = false;
				result.ErrorMessage = string.Join(System.Environment.NewLine,
					emitResult.Diagnostics
						.Where(d => d.Severity == DiagnosticSeverity.Error)
						.Select(d => $"[{d.Id}] {d.GetMessage()}"));
				return result;
			}

			alc = new UnloadableScriptContext(_sharedAssemblies);
			ms.Seek(0, SeekOrigin.Begin);
			var assembly = alc.LoadFromStream(ms);
			var scriptWrapperType = assembly.GetType("ScriptWrapper");
			var runMethod = scriptWrapperType?.GetMethod("RunScript", BindingFlags.Public | BindingFlags.Static);

			result.ReturnValue = runMethod?.Invoke(null, [globals]);
			result.IsSuccess = true;
			return result;
		}
		catch (TargetInvocationException e) when (e.InnerException != null)
		{
			result.IsSuccess = false;
			result.ErrorMessage = $"ERROR: {e.InnerException.GetType().Name}: {e.InnerException.Message}";
			return result;
		}
		catch (Exception e)
		{
			result.IsSuccess = false;
			result.ErrorMessage = $"ERROR: {e.GetType().Name}: {e.Message}";
			return result;
		}
		finally
		{
			if (alc != null && alc.IsCollectible)
			{
				alc.Unload();
				GC.Collect();
				GC.WaitForPendingFinalizers();
			}
		}
	}

	public string TryAutoComplete(string partialCommand, Node scene)
	{
		string nodePath;
		string postFix;
		_GetRegexCommandLastNodePathAndPostFix(partialCommand, out nodePath, out postFix);

		Node node = scene.GetNodeOrNull(nodePath);
		Type type = node.GetType();
		BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

		var props = type.GetProperties(flags).Select(p => p.Name);

		var fields = type.GetFields(flags)
			.Where(f => !f.IsDefined(typeof(CompilerGeneratedAttribute), false))
			.Select(f => f.Name);

		var methods = type.GetMethods(flags)
			.Where(m => !m.IsSpecialName)
			.Select(m => m.Name);

		var result = props.Concat(fields).Concat(methods)
			.Distinct()
			.OrderBy(n => n)
			.Where(name => name.StartsWith(postFix, StringComparison.Ordinal))
			.ToList();

		return string.Join(' ', result);
	}

	private MetadataReference _GetReference(Assembly assembly)
		=> (assembly.Location == "")
			? AssemblyMetadata.Create(_GetMetadata(assembly)).GetReference()
			: MetadataReference.CreateFromFile(assembly.Location);

	private ModuleMetadata _GetMetadata(Assembly assembly)
	{
		unsafe
		{
			return assembly.TryGetRawMetadata(out var blob, out var len)
				? ModuleMetadata.CreateFromMetadata((IntPtr)blob, len)
				: throw new InvalidOperationException($"Could not get metadata from {assembly.FullName}");
		}
	}
}
