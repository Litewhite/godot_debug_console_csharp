namespace GodotDebugConsole;

using Godot;
using SysStrList = System.Collections.Generic.List<string>;

public partial class DebugConsoleController : Control
{
	private DebugConsoleTerminal _terminal;
	private LineEdit _commandInput;
	private TextEdit _resultViewer;
	private SysStrList _historyCommands;
	private int _historyCommandsIndex = 0;
	private MultiTypeDict _localVars;

	public override void _Ready()
	{
		_terminal = new DebugConsoleTerminal();
		_terminal.InitTerminal();
		_commandInput = GetNode<LineEdit>("CommandInput");
		_commandInput.GuiInput += _OnCommandInputGuiInput;
		_resultViewer = GetNode<TextEdit>("Bottom/BottomLineOffset/ResultViewer");

		_historyCommands = new SysStrList();
		_historyCommandsIndex = 0;

		_localVars = new MultiTypeDict();
	}

	public override void _Process(double delta)
	{

	}

	public override void _Input(InputEvent @event)
	{
		if (Visible && @event is InputEventKey keyEvent && keyEvent.Pressed)
		{
			if (keyEvent.Keycode == Key.Up)
			{
				GetViewport().SetInputAsHandled();
				_UpdateHistory(true);
				return;
			}
			else if (keyEvent.Keycode == Key.Down)
			{
				GetViewport().SetInputAsHandled();
				_UpdateHistory(false);
				return;
			}
			if (keyEvent.Keycode == Key.Tab)
			{
				AcceptEvent(); 
				_HandleAutoComplete();
			}
		}
		base._Input(@event);
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event is InputEventKey keyEvent && keyEvent.Pressed)
		{
			if (keyEvent.Keycode == Key.Quoteleft)
			{
				GetViewport().SetInputAsHandled();
				_ToggleTerminal();
				return;
			}
		}
	}

	private void _ToggleTerminal()
	{
		Visible = !Visible;
		// Handle console open and mouse capture thing here
		// G.IsConsoleOpen = Visible;
		// G.MouseCaptured = !Visible;
		if (Visible)
		{
			_commandInput.GrabFocus();
		}
	}

	private ScriptGlobals _MakeGlobals()
	{
		var globals = new ScriptGlobals
		{
			SCENE = GetTree().CurrentScene,
			VARS = _localVars
		};
		return globals;
	}

	private void _AppendToHistory(string command)
	{
		_historyCommands.Add(command);
		_historyCommandsIndex = _historyCommands.Count;
	}

	private void _UpdateHistory(bool isUp)
	{
		if (isUp)
		{
			_historyCommandsIndex--;
		}
		else
		{
			_historyCommandsIndex++;
		}
		_historyCommandsIndex = Mathf.Clamp(_historyCommandsIndex, 0, _historyCommands.Count);
		if (_historyCommandsIndex < _historyCommands.Count)
		{
			_commandInput.Text = _historyCommands[_historyCommandsIndex];
		}
		else
		{
			_commandInput.Text = "";
		}
		_commandInput.CaretColumn = _commandInput.Text.Length;
	}

	private void _ExecuteAndAppendResult(string command)
	{
		var globals = _MakeGlobals();
		var result = _terminal.EvaluateCommand(command, globals);
		_resultViewer.Text += ">>> " + command + "\n" + result.ToString() + "\n";
		// scroll to bottom
		var vBar = _resultViewer.GetVScrollBar();
		vBar.Value = vBar.MaxValue;
	}

	private void _HandleAutoComplete()
	{
		string autoCompleteStr = _terminal.TryAutoComplete(_commandInput.Text, GetTree().CurrentScene);
		if (autoCompleteStr.Length == 0)
		{
			return;
		}
		_resultViewer.Text += "AutoComp: " + autoCompleteStr + "\n";
		// scroll to bottom
		var vBar = _resultViewer.GetVScrollBar();
		vBar.Value = vBar.MaxValue;
	}

#region Connect

	private void _OnCommandInputTextSubmitted(string command)
	{
		_ExecuteAndAppendResult(command);
		_AppendToHistory(command);
		_commandInput.Text = "";
	}

#endregion

#region Callback

	private void _OnCommandInputGuiInput(InputEvent @event)
	{
		if (Visible && @event is InputEventKey keyEvent && keyEvent.Pressed)
		{
			if (keyEvent.Keycode == Key.Quoteleft)
			{
				GetViewport().SetInputAsHandled();
				_commandInput.ReleaseFocus();
				_ToggleTerminal();
				return;
			}
		}
	}

#endregion
}
