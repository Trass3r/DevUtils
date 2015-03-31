using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;

namespace VSPackage.DevUtils
{
	internal static class TaskManager
	{
		private static ErrorListProvider errorListProvider;

		public static void initialize(IServiceProvider serviceProvider)
		{
			errorListProvider = new ErrorListProvider(serviceProvider);
		}

		public static ErrorTask addError(string message)
		{
			return addTask(message, TaskErrorCategory.Error);
		}

		public static ErrorTask addWarning(string message)
		{
			return addTask(message, TaskErrorCategory.Warning);
		}

		public static ErrorTask addMessage(string message)
		{
			return addTask(message, TaskErrorCategory.Message);
		}

		private static ErrorTask addTask(string message, TaskErrorCategory category)
		{
			var t = new ErrorTask
			{
				Category = TaskCategory.User,
				ErrorCategory = category,
				Text = message
			};
			errorListProvider.Tasks.Add(t);
			return t;
		}
	}
}
