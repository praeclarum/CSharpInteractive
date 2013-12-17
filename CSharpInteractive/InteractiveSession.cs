using System;
using MonoDevelop.Components;
using MonoDevelop.Ide.Gui;
using Gdk;
using MonoDevelop.Ide;
using MonoDevelop.Core;
using MonoDevelop.Components.Commands;
using System.Diagnostics;
using System.Collections.Generic;
using System.Linq;

namespace CSharpInteractive
{
	public interface IInteractiveSession
	{
		event Action<string> TextReceived;
		event Action PromptReady;
		event Action Exited;
		void StartReceiving ();
		void SendCommand (string line);
		void Interrupt ();
	}

	public abstract class ProcessInteractiveSession : IInteractiveSession
	{
		public event Action<string> TextReceived = delegate {};
		public event Action PromptReady = delegate {};
		public event Action Exited = delegate {};

		Process process;

		public abstract string GetFileName ();
		public virtual IEnumerable<string> GetArguments ()
		{
			yield break;
		}

		public void StartReceiving ()
		{
			var si = new ProcessStartInfo {
				FileName = GetFileName (),
				Arguments = string.Join (" ", GetArguments ().Select (x => "\"" + x + "\"")),
				UseShellExecute = false,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				RedirectStandardInput = true,
			};

			process = new Process ();

			process.StartInfo = si;

			process.EnableRaisingEvents = true;

			process.Exited += (sender, e) => {
				process = null;
				Exited ();
			};

			process.OutputDataReceived += (sender, e) => {
				TextReceived (e.Data);
				PromptReady ();
			};

			process.ErrorDataReceived += (sender, e) => {
				TextReceived (e.Data);
				PromptReady ();
			};

			process.Start ();

			process.BeginOutputReadLine ();
			process.BeginErrorReadLine ();
		}

		public void SendCommand (string line)
		{
			if (process == null)
				return;

			process.StandardInput.WriteLine (line);
		}

		public void Interrupt ()
		{
			if (process == null)
				return;

			process.Kill ();
		}
	}

	public class CSharpInteractiveSession : ProcessInteractiveSession
	{
		public override string GetFileName ()
		{
			return "/Library/Frameworks/Mono.framework/Versions/Current/bin/csharp";
		}
	}	
}
