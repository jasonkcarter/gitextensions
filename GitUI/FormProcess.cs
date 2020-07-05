﻿using System;
using System.Collections.Generic;
using System.Windows.Forms;
using GitCommands;
using GitUI.UserControls;
using GitUIPluginInterfaces;
using JetBrains.Annotations;

namespace GitUI
{
    /// <param name="isError">if command finished with error</param>
    /// <param name="form">this form</param>
    /// <returns>if handled</returns>
    public delegate bool HandleOnExit(ref bool isError, FormProcess form);

    public class FormProcess : FormStatus
    {
        public string Remote { get; set; }
        public string ProcessString { get; set; }
        public string ProcessArguments { get; set; }
        public Func<string, string> ProcessArgumentsFilter { get; set; }
        public string ProcessInput { get; set; }
        public readonly string WorkingDirectory;
        public HandleOnExit HandleOnExitCallback { get; set; }
        public readonly Dictionary<string, string> ProcessEnvVariables = new Dictionary<string, string>();

        protected FormProcess()
            : base(true)
        {
        }

        public FormProcess(ConsoleOutputControl outputControl, [CanBeNull] string process, ArgumentString arguments, string workingDirectory, string input, bool useDialogSettings, Func<string, string> argumentsFilter = null)
            : base(outputControl, useDialogSettings)
        {
            ProcessCallback = ProcessStart;
            AbortCallback = ProcessAbort;
            ProcessString = process ?? AppSettings.GitCommand;
            ProcessArguments = arguments;
            ProcessArgumentsFilter = argumentsFilter;
            Remote = "";
            ProcessInput = input;
            WorkingDirectory = workingDirectory;

            var displayPath = PathUtil.GetDisplayPath(WorkingDirectory);
            if (!string.IsNullOrWhiteSpace(displayPath))
            {
                Text += $" ({displayPath})";
            }

            ConsoleOutput.ProcessExited += delegate { OnExit(ConsoleOutput.ExitCode); };
            ConsoleOutput.DataReceived += DataReceivedCore;
        }

        public FormProcess(string process, ArgumentString arguments, string workingDirectory, string input, bool useDialogSettings, Func<string, string> argumentsFilter = null)
            : this(null, process, arguments, workingDirectory, input, useDialogSettings, argumentsFilter)
        {
        }

        public static bool ShowDialog([CanBeNull] IWin32Window owner, GitModule module, ArgumentString arguments)
        {
            return ShowDialog(owner, module.GitExecutable.ToString(), arguments, module.WorkingDir, null, true, module.GitArgumentsFilter);
        }

        public static bool ShowDialog([CanBeNull] IWin32Window owner, GitModule module, string process, ArgumentString arguments, Func<string, string> argumentsFilter = null)
        {
            return ShowDialog(owner, process, arguments, module.WorkingDir, null, true, argumentsFilter);
        }

        public static bool ShowDialog(GitModuleForm owner, ArgumentString arguments)
        {
            return ShowDialog(owner, owner.Module.GitExecutable.ToString(), arguments, owner.Module.GitArgumentsFilter);
        }

        public static bool ShowDialog(GitModuleForm owner, string process, ArgumentString arguments, Func<string, string> argumentsFilter = null)
        {
            return ShowDialog(owner, process, arguments, owner.Module.WorkingDir, null, true, argumentsFilter);
        }

        public static bool ShowDialog(GitModuleForm owner, ArgumentString arguments, bool useDialogSettings)
        {
            return ShowDialog(owner, owner.Module, arguments, useDialogSettings);
        }

        public static bool ShowDialog([CanBeNull] IWin32Window owner, GitModule module, ArgumentString arguments, bool useDialogSettings)
        {
            return ShowDialog(owner, module.GitExecutable.ToString(), arguments, module.WorkingDir, null, useDialogSettings, module.GitArgumentsFilter);
        }

        public static bool ShowDialog([CanBeNull] IWin32Window owner, string process, ArgumentString arguments, string workingDirectory, string input, bool useDialogSettings, Func<string, string> argumentsFilter = null)
        {
            using (var formProcess = new FormProcess(process, arguments, workingDirectory, input, useDialogSettings, argumentsFilter))
            {
                formProcess.ShowDialog(owner);
                return !formProcess.ErrorOccurred();
            }
        }

        public static bool ShowStandardProcessDialog([CanBeNull] IWin32Window owner, string process, ArgumentString arguments, string workingDirectory, string input, bool useDialogSettings, Func<string, string> argumentsFilter = null)
        {
            var outputCtrl = new EditboxBasedConsoleOutputControl();
            using (var formProcess = new FormProcess(outputCtrl, process, arguments, workingDirectory, input, useDialogSettings, argumentsFilter))
            {
                formProcess.ShowDialog(owner);
                return !formProcess.ErrorOccurred();
            }
        }

        public static FormProcess ShowModeless([CanBeNull] IWin32Window owner, string process, ArgumentString arguments, string workingDirectory, string input, bool useDialogSettings, Func<string, string> argumentsFilter = null)
        {
            var formProcess = new FormProcess(process, arguments, workingDirectory, input, useDialogSettings, argumentsFilter)
            {
                ControlBox = true
            };

            formProcess.Show(owner);

            return formProcess;
        }

        public static FormProcess ShowModeless(GitModuleForm owner, ArgumentString arguments)
        {
            return ShowModeless(owner, owner.Module.GitExecutable.ToString(), arguments, owner.Module.WorkingDir, null, true, owner.Module.GitArgumentsFilter);
        }

        public static string ReadDialog(GitModuleForm owner, ArgumentString arguments)
        {
            return ReadDialog(owner, owner.Module.GitExecutable.ToString(), arguments, owner.Module, null, true, owner.Module.GitArgumentsFilter);
        }

        public static string ReadDialog([CanBeNull] IWin32Window owner, string process, ArgumentString arguments, GitModule module, string input, bool useDialogSettings, Func<string, string> argumentsFilter = null)
        {
            using (var formProcess = new FormProcess(process, arguments, module.WorkingDir, input, useDialogSettings, argumentsFilter))
            {
                formProcess.ShowDialog(owner);
                return formProcess.GetOutputString();
            }
        }

        protected virtual void BeforeProcessStart()
        {
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            switch (keyData)
            {
                case Keys.Escape:
                    {
                        Close();
                        return true;
                    }

                default:
                    {
                        return base.ProcessCmdKey(ref msg, keyData);
                    }
            }
        }

        private void ProcessStart(FormStatus form)
        {
            BeforeProcessStart();
            string quotedProcessString = ProcessString;
            if (quotedProcessString.IndexOf(' ') != -1)
            {
                quotedProcessString = quotedProcessString.Quote();
            }

            AddMessageLine(quotedProcessString + " " + ProcessArguments);

            try
            {
                string arguments = ProcessArgumentsFilter == null ? ProcessArguments : ProcessArgumentsFilter(ProcessArguments);
                ConsoleOutput.StartProcess(ProcessString, arguments, WorkingDirectory, ProcessEnvVariables);

                if (!string.IsNullOrEmpty(ProcessInput))
                {
                    throw new NotSupportedException("No non-NULL usages of ProcessInput are currently expected.");  // Not implemented with all terminal variations, so let's postpone until there's at least one non-null case
/*
                    Thread.Sleep(500);
                    Process.StandardInput.Write(ProcessInput);
                    AddMessageLine(string.Format(":: Wrote [{0}] to process!\r\n", ProcessInput));
*/
                }
            }
            catch (Exception e)
            {
                AddMessageLine("\n" + e.ToStringWithData());
                OnExit(1);
            }
        }

        private void ProcessAbort(FormStatus form)
        {
            KillProcess();
        }

        protected void KillProcess()
        {
            try
            {
                ConsoleOutput.KillProcess();

                var module = new GitModule(WorkingDirectory);
                module.UnlockIndex(includeSubmodules: true);
            }
            catch
            {
                // no-op
            }
        }

        /// <param name="isError">if command finished with error</param>
        /// <returns>if handled</returns>
        protected virtual bool HandleOnExit(ref bool isError)
        {
            return HandleOnExitCallback != null && HandleOnExitCallback(ref isError, this);
        }

        private void OnExit(int exitcode)
        {
            this.InvokeAsync(() =>
            {
                bool isError;
                try
                {
                    isError = exitcode != 0;

                    if (HandleOnExit(ref isError))
                    {
                        return;
                    }
                }
                catch
                {
                    isError = true;
                }

                Done(!isError);
            }).FileAndForget();
        }

        protected virtual void DataReceived(object sender, TextEventArgs e)
        {
        }

        private void DataReceivedCore(object sender, TextEventArgs e)
        {
            if (e.Text.Contains("%") || e.Text.Contains("remote: Counting objects"))
            {
                ThreadHelper.JoinableTaskFactory.RunAsync(() => SetProgressAsync(e.Text)).FileAndForget();
            }
            else
            {
                const string ansiSuffix = "\u001B[K";
                string line = e.Text.Replace(ansiSuffix, "");

                if (ConsoleOutput.IsDisplayingFullProcessOutput)
                {
                    OutputLog.Append(line); // To the log only, display control displays it by itself
                }
                else
                {
                    AppendOutput(line); // Both to log and display control
                }
            }

            DataReceived(sender, e);
        }

        /// <summary>
        /// Appends a line of text (CRLF added automatically) both to the logged output (<see cref="FormStatus.GetOutputString"/>) and to the display console control.
        /// </summary>
        public void AppendOutput(string line)
        {
            // To the internal log (which can be then retrieved as full text from this form)
            OutputLog.Append(line);

            // To the display control
            AddMessage(line);
        }

        public static bool IsOperationAborted(string dialogResult)
        {
            return dialogResult.Trim('\r', '\n') == "Aborted";
        }
    }
}
