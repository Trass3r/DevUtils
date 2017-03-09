using System;
using System.Windows;
using System.Windows.Shell;
using EnvDTE;

namespace DevUtils
{
	internal class BuildEventsHandler
	{
		DevUtilsPackage _vspkg;
		BuildEvents     _buildEvents; // got to cache this object to get events
		DateTime        _buildStartTime;
		TaskbarItemInfo _taskbarItemInfo;
		uint            _numBuiltProjects     = 0;
		uint            _numProjectsToBeBuilt = 0; // cached

		// TODO: need to check if this cached item is still the active one?
		TaskbarItemInfo taskbarItemInfo { get { return _taskbarItemInfo; } }

		public BuildEventsHandler(DevUtilsPackage pkg)
		{
			_vspkg = pkg;
			_buildEvents = pkg.dte.Events.BuildEvents;
			_buildEvents.OnBuildBegin += buildStarted;
			_buildEvents.OnBuildDone  += buildDone;
			_buildEvents.OnBuildProjConfigDone  += buildProjDone;

			_taskbarItemInfo = Application.Current.MainWindow.TaskbarItemInfo;
			if (_taskbarItemInfo == null)
				Application.Current.MainWindow.TaskbarItemInfo = _taskbarItemInfo = new TaskbarItemInfo();
		}

#region Event Handlers
		// call every build as it could be a different solution
		private uint numProjectsToBeBuilt()
		{
			SolutionContexts solutionContexts = _vspkg.dte.Solution.SolutionBuild.ActiveConfiguration.SolutionContexts;
			uint num = 0;
			for (int i = 1; i <= solutionContexts.Count; ++i)
				if (solutionContexts.Item(i).ShouldBuild)
					++num;
			return num;
		}

		private void buildStarted(vsBuildScope scope, vsBuildAction action)
		{
			// fetch the number of projects just once
			_numProjectsToBeBuilt = numProjectsToBeBuilt();

			_numBuiltProjects = 0;
			taskbarItemInfo.ProgressValue = 0;
			taskbarItemInfo.ProgressState = TaskbarItemProgressState.Indeterminate;

			if (scope != vsBuildScope.vsBuildScopeSolution)
				return;

			_buildStartTime = DateTime.Now;
		}

		private void buildDone(vsBuildScope scope, vsBuildAction action)
		{
			// fill it up
			taskbarItemInfo.ProgressValue = 1;

			// keep it red
			if (taskbarItemInfo.ProgressState != TaskbarItemProgressState.Error)
				taskbarItemInfo.ProgressState = TaskbarItemProgressState.None;

			if (scope != vsBuildScope.vsBuildScopeSolution)
				return;

			string msg = String.Format("Total build time: {0}", DateTime.Now - _buildStartTime);
			_vspkg.writeToBuildWindow("\n" + msg);
			_vspkg.writeStatus(msg);
		}

		private void buildProjDone(string project, string projectConfig, string platform, string solutionConfig, bool success)
		{
			++_numBuiltProjects;

			// switch to normal mode
			if (_numBuiltProjects == 1)
				taskbarItemInfo.ProgressState = TaskbarItemProgressState.Normal;

			if (!success)
				taskbarItemInfo.ProgressState = TaskbarItemProgressState.Error;

			taskbarItemInfo.ProgressValue = (double)_numBuiltProjects / _numProjectsToBeBuilt;
		}
#endregion
	}
}
