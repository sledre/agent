﻿using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading.Tasks;

namespace SledREAgent
{
	public abstract class Job
	{
		protected Logger logger;
		protected WorkerTask workerTask;
		public Job(Logger _logger, WorkerTask _workerTask)
		{
			logger = _logger;
			workerTask = _workerTask;
		}

		// Consider using a timeout in your job execution methode
		public abstract void ExecuteJob();

		// Treat results of your executed job
		public abstract void TreatResults();

		// Must be async when implementing this method
		public abstract Task<bool> SubmitResults(String workerId, HttpClient client);
	}
}