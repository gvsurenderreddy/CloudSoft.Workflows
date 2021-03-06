﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Activities;

namespace CloudSoft.Workflows
{
	internal class WorkItem : IDisposable
	{
		private ManualResetEvent m_manualResetEvent;
		private System.Activities.Activity m_Activity;
		Dictionary<string, object> m_Parameters;
		Action<Dictionary<string, object>> m_Completed;
		ProgressReporter m_ProgressReporter;
		Action m_Finally;
		Action m_Aborted;

		public WorkItem(ProgressReporter pr, ManualResetEvent mre, System.Activities.Activity activity, Dictionary<string, object> parameters, Action<Dictionary<string, object>> completed, Action<Exception> failed, Action final, Action aborted)
		{
			m_manualResetEvent = mre;
			m_Activity = activity;
			m_Parameters = parameters;
			m_Completed = completed;
			Failed = failed;
			m_ProgressReporter = pr;
			m_Finally = final;
			m_Aborted = aborted;
		}

		internal Action<Exception> Failed { get; set; }

		public void Run()
		{
			var workflowApplication = new System.Activities.WorkflowApplication(m_Activity, m_Parameters ?? new Dictionary<string, object>());
			workflowApplication.Extensions.Add(m_ProgressReporter);
			workflowApplication.Completed = (WorkflowApplicationCompletedEventArgs arg) =>
			{
				try
				{
					m_ProgressReporter.OnFinish();
					if (m_Completed != null)
					{
						var dic = new Dictionary<string, object>();
						foreach (var outputParameter in arg.Outputs)
						{
							dic.Add(outputParameter.Key, outputParameter.Value);
						}

						m_Completed.Invoke(dic);
					}
				}
				catch (Exception ex)
				{
					m_ProgressReporter.ErrorStack = ex.ToString();
				}
				finally
				{
					if (!m_manualResetEvent.SafeWaitHandle.IsClosed)
					{
						m_manualResetEvent.Set();
					}
					m_ProgressReporter.TerminatedDate = DateTime.Now;
				}
			};

			workflowApplication.OnUnhandledException = delegate(WorkflowApplicationUnhandledExceptionEventArgs arg)
			{
				m_ProgressReporter.ErrorStack = arg.UnhandledException.ToString();
				if (Failed != null)
				{
					Failed.Invoke(arg.UnhandledException);
				}
				m_ProgressReporter.TerminatedDate = DateTime.Now;
				m_manualResetEvent.Set();
				return UnhandledExceptionAction.Terminate;
			};

			workflowApplication.Aborted = (WorkflowApplicationAbortedEventArgs arg) =>
			{
				if (m_Aborted!= null)
				{
					m_Aborted.Invoke();
				}
			};


			try
			{
				workflowApplication.Run();
				m_ProgressReporter.OnStart();
				m_ProgressReporter.StartDate = DateTime.Now;
			}
			catch (Exception ex)
			{
				m_ProgressReporter.ErrorStack = ex.ToString();
				m_ProgressReporter.TerminatedDate = DateTime.Now;
				if (Failed != null)
				{
					Failed.Invoke(ex);
				}
				m_manualResetEvent.Set();
			}

			m_manualResetEvent.WaitOne();

			try
			{
				if (m_Finally != null)
				{
					m_Finally.Invoke();
				}
			}
			catch (Exception ex)
			{
				if (Failed != null)
				{
					Failed.Invoke(ex);
				}
			}

			Dispose();
		}

		#region IDisposable Members

		public void Dispose()
		{
			m_Activity = null;
			m_Completed = null;
			Failed = null;
			m_Finally = null;
			if (m_manualResetEvent != null)
			{
				m_manualResetEvent.Dispose();
			}
			m_Parameters = null;
		}

		#endregion
	}
}
