//------------------------------------------------------------------------------
// <copyright file="RunCommand.cs" company="Company">
//   Copyright (c) Company.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace CodeRunner
{
  /// <summary>
  /// Command handler
  /// </summary>
  internal sealed class RunCommand
  {
    /// <summary>
    /// Command ID.
    /// </summary>
    public const int CommandId = 0x0100;

    /// <summary>
    /// Command menu group (command set GUID).
    /// </summary>
    public static readonly Guid CommandSet = new Guid("b62d762c-0f40-4249-94cb-7a09ca719bda");

    /// <summary>
    /// VS Package that provides this command, not null.
    /// </summary>
    private readonly Package package;

    /// <summary>
    /// Initializes a new instance of the <see cref="RunCommand"/> class.
    /// Adds our command handlers for menu (commands must exist in the command table file)
    /// </summary>
    /// <param name="package">Owner package, not null.</param>
    private RunCommand(Package package)
    {
      if (package == null)
      {
        throw new ArgumentNullException("package");
      }

      this.package = package;

      OleMenuCommandService commandService = this.ServiceProvider.GetService(typeof(IMenuCommandService)) as OleMenuCommandService;
      if (commandService != null)
      {
        var menuCommandID = new CommandID(CommandSet, CommandId);
        var menuItem = new MenuCommand(this.Run, menuCommandID);
        commandService.AddCommand(menuItem);
      }
    }

    /// <summary>
    /// Gets the instance of the command.
    /// </summary>
    public static RunCommand Instance
    {
      get;
      private set;
    }

    /// <summary>
    /// Gets the service provider from the owner package.
    /// </summary>
    private IServiceProvider ServiceProvider
    {
      get
      {
        return this.package;
      }
    }

    /// <summary>
    /// Initializes the singleton instance of the command.
    /// </summary>
    /// <param name="package">Owner package, not null.</param>
    public static void Initialize(Package package)
    {
      Instance = new RunCommand(package);
    }

    /// <summary>
    /// Helper to get or create the Output Window pane named "Code Runner".
    /// </summary>
    private OutputWindowPane GetOutputPane(DTE2 dte)
    {
      var ow = dte.ToolWindows.OutputWindow;
      try
      {
        return ow.OutputWindowPanes.Item("Code Runner");
      }
      catch
      {
        ow.OutputWindowPanes.Add("Code Runner");
        return ow.OutputWindowPanes.Item("Code Runner");
      }
    }

    /// <summary>
    /// This function is the callback used to execute the command when the menu item is clicked.
    /// </summary>
    private void Run(object sender, EventArgs e)
    {
      var dte = ServiceProvider.GetService(typeof(DTE)) as DTE2;
      string selectedPath = null;

      if (dte.ActiveWindow.Type == vsWindowType.vsWindowTypeDocument)
      {
        selectedPath = dte.ActiveDocument.FullName;
      }
      else if (dte.SelectedItems.Count == 1)
      {
        selectedPath = dte.SelectedItems?.Item(1)?.ProjectItem?.FileNames[0];
      }

      if (string.IsNullOrEmpty(selectedPath))
        return;

      string ext = Path.GetExtension(selectedPath).ToLowerInvariant();

      // Telemetry call (no-op if key not configured)
      AppInsightsClient.trackEvent(ext);

      var extMapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
      {
        {".js", "node"},
        {".php", "php"},
        {".py", "python"},
        {".pl", "perl"},
        {".rb", "ruby"},
        {".go", "go run"},
        {".lua", "lua"},
        {".groovy", "groovy"},
        {".scala", "scala"},
        {".vbs", "cscript //Nologo"},
        {".ps1", "powershell -ExecutionPolicy ByPass -File"}
      };

      if (!extMapping.ContainsKey(ext))
      {
        VsShellUtilities.ShowMessageBox(
          this.ServiceProvider,
          $"The file type \"{ext}\" is not supported.",
          "File type not supported",
          OLEMSGICON.OLEMSGICON_INFO,
          OLEMSGBUTTON.OLEMSGBUTTON_OK,
          OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
        return;
      }

      string mapping = extMapping[ext];
      // split mapping into executable and arguments (first space)
      string exe;
      string mappedArgs;
      int idx = mapping.IndexOf(' ');
      if (idx >= 0)
      {
        exe = mapping.Substring(0, idx);
        mappedArgs = mapping.Substring(idx + 1);
      }
      else
      {
        exe = mapping;
        mappedArgs = string.Empty;
      }

      // Build final args: mappedArgs + filename (quoted)
      string fileNameOnly = Path.GetFileName(selectedPath);
      string arguments = string.IsNullOrWhiteSpace(mappedArgs) ? $"\"{fileNameOnly}\"" : $"{mappedArgs} \"{fileNameOnly}\"";

      var startInfo = new ProcessStartInfo
      {
        FileName = exe,
        Arguments = arguments,
        WorkingDirectory = Path.GetDirectoryName(selectedPath),
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
      };

      var pane = GetOutputPane(dte);
      // Write a header line
      ThreadHelper.Generic.BeginInvoke(() => pane.OutputString($"--- Code Runner: `{exe} {arguments}` (cwd: {startInfo.WorkingDirectory}) ---{Environment.NewLine}"));

      // Launch and stream output on a background task
      Task.Run(() =>
      {
        try
        {
          using (var process = new Process() { StartInfo = startInfo, EnableRaisingEvents = true })
          {
            process.OutputDataReceived += (s, ea) =>
            {
              if (ea.Data != null)
              {
                ThreadHelper.Generic.BeginInvoke(() => pane.OutputString(ea.Data + Environment.NewLine));
              }
            };
            process.ErrorDataReceived += (s, ea) =>
            {
              if (ea.Data != null)
              {
                ThreadHelper.Generic.BeginInvoke(() => pane.OutputString("[ERR] " + ea.Data + Environment.NewLine));
              }
            };

            bool started = process.Start();
            if (!started)
            {
              ThreadHelper.Generic.BeginInvoke(() =>
                VsShellUtilities.ShowMessageBox(
                  this.ServiceProvider,
                  $"Failed to start process: {exe}",
                  "Run Code Error",
                  OLEMSGICON.OLEMSGICON_CRITICAL,
                  OLEMSGBUTTON.OLEMSGBUTTON_OK,
                  OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST));
              return;
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            process.WaitForExit();

            ThreadHelper.Generic.BeginInvoke(() => pane.OutputString($"--- Process exited with code {process.ExitCode} ---{Environment.NewLine}"));
          }
        }
        catch (Exception ex)
        {
          ThreadHelper.Generic.BeginInvoke(() =>
          {
            VsShellUtilities.ShowMessageBox(
              this.ServiceProvider,
              $"Failed to start or monitor process: {ex.Message}",
              "Run Code Error",
              OLEMSGICON.OLEMSGICON_CRITICAL,
              OLEMSGBUTTON.OLEMSGBUTTON_OK,
              OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
          });
        }
      });
    }
  }
}
