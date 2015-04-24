using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace VSPackage.DevUtils
{
	internal static class Util
	{
		public static string getBuildOutputFromOutputWindow(DTE2 dte)
		{
			OutputWindowPane outputWindowPane = null;
			foreach (OutputWindowPane outputWindowPane1 in dte.ToolWindows.OutputWindow.OutputWindowPanes)
			{
				if (outputWindowPane1.Name == "Build")
				{
					outputWindowPane = outputWindowPane1;
					break;
				}
			}
			if (outputWindowPane == null)
				return "";

			outputWindowPane.Activate();

			TextSelection selection = outputWindowPane.TextDocument.Selection;
			selection.StartOfDocument(false);
			selection.EndOfDocument(true);
			string text = selection.Text;
			return text;
		}
	}

	class BuildEventsHandler
	{
		DevUtilsPackage _vspkg;
		// got to cache this object to get events
		BuildEvents _buildEvents;
		DateTime _buildStartTime;

		public BuildEventsHandler(DevUtilsPackage pkg)
		{
			_vspkg = pkg;
			_buildEvents = pkg.dte.Events.BuildEvents;
			_buildEvents.OnBuildBegin += buildStarted;
			_buildEvents.OnBuildDone  += buildDone;
			_buildEvents.OnBuildProjConfigBegin += buildProjStarted;
			_buildEvents.OnBuildProjConfigDone  += buildProjDone;

			_buildStartTime = new DateTime(0);
		}

#region Event Handlers
		private void buildStarted(vsBuildScope scope, vsBuildAction action)
		{
			_buildStartTime = DateTime.Now;
		}

		private void buildDone(vsBuildScope scope, vsBuildAction action)
		{
			// if everything was up-to-date there was no start event
			if (_buildStartTime.Ticks == 0)
				return;

			string msg = String.Format("Total build time: {0}", DateTime.Now - _buildStartTime);
			_vspkg.writeToBuildWindow("\n" + msg);
			_vspkg.writeStatus(msg);

			_buildStartTime = new DateTime(0);

			string output = Util.getBuildOutputFromOutputWindow(_vspkg.dte);
			var matches = Regex.Matches(output, @">?\s+([^:]+:[^:]+)\s:\sinfo\sC5002:\sloop\snot\svectorized\sdue\sto\sreason\s\'(\d+)\'");
			foreach (Match match in matches)
			{
				string nameAndLine = match.Groups[1].Value.Trim();
				Match match2 = Regex.Match(nameAndLine, @"(.*)\((\d+)\)");
				ErrorTask task = TaskManager.addMessage(match.Value);
				task.Document = match2.Groups[1].Value;
				task.Line = int.Parse(match2.Groups[2].Value);
			}
		}

		void buildProjStarted(string project, string projectConfig, string platform, string solutionConfig)
		{
			_vspkg.writeStatus(String.Format("buildProjStarted {0} {1} {2} {3}", project, projectConfig, platform, solutionConfig));
		}

		void buildProjDone(string project, string projectConfig, string platform, string solutionConfig, bool success)
		{
			_vspkg.writeStatus(String.Format("buildProjDone {0} {1} {2} {3} success: {4}", project, projectConfig, platform, solutionConfig, success));
		}
#endregion
	}
}
