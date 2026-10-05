namespace Loupedeck.LogiToolboxPlugin.Actions
{
    using System;
    using System.Diagnostics;
    using System.IO;

    // Opens a program, file, folder or URL through the Windows shell, so anything that works in
    // the Run dialog works here. Each button has its own target and optional arguments.
    public class LaunchCommand : ActionEditorCommand
    {
        private const String TargetControlName = "target";
        private const String ArgumentsControlName = "arguments";

        public LaunchCommand()
        {
            this.DisplayName = "Launch App / URL";
            this.Description = "Opens a program, file, folder or web address";
            this.GroupName = "System";

            this.ActionEditor.AddControlEx(new ActionEditorTextbox(TargetControlName, "Target", "Program, file, folder or URL to open").SetRequired());
            this.ActionEditor.AddControlEx(new ActionEditorTextbox(ArgumentsControlName, "Arguments", "Optional command-line arguments (programs only)"));
        }

        protected override Boolean RunCommand(ActionEditorActionParameters actionParameters)
        {
            var target = Environment.ExpandEnvironmentVariables(actionParameters.GetString(TargetControlName, String.Empty).Trim().Trim('"'));
            var arguments = actionParameters.GetString(ArgumentsControlName, String.Empty).Trim();

            if (target.Length == 0)
            {
                return false;
            }

            try
            {
                Process.Start(new ProcessStartInfo(target) { UseShellExecute = true, Arguments = arguments })?.Dispose();

                return true;
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, $"Failed to launch {target}");

                return false;
            }
        }

        protected override String GetCommandDisplayName(ActionEditorActionParameters actionParameters) =>
            GetLabel(actionParameters.GetString(TargetControlName, String.Empty));

        // A short name for the button: the host for a URL, otherwise the file or folder name.
        private static String GetLabel(String target)
        {
            target = target.Trim().Trim('"');

            if (target.Length == 0)
            {
                return "Launch";
            }

            if (Uri.TryCreate(target, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            {
                return uri.Host;
            }

            var name = Path.GetFileNameWithoutExtension(target.TrimEnd('\\', '/'));

            return String.IsNullOrEmpty(name) ? target : name;
        }
    }
}
