using Newtonsoft.Json;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.ServiceProcess;
using System.Text;
using System.Threading.Tasks;
using System.Timers;
using st = System.Threading;

namespace AutoDetoursAgent
{
    public partial class AgentService : ServiceBase
    {
        private Timer timer_agent = new Timer();

        private HttpClient client = new HttpClient();
        private Worker worker = new Worker();
        private WorkerTask workerTask = new WorkerTask();
        private AgentState state = AgentState.INIT;

        private bool customMutex = true;
        private bool isVM;

        private Process syelogd = new Process();
        private Process withdll = new Process();

        private Process malunpack = new Process();
        private Process tar = new Process();
        private String apiBaseURL;
        private String defaultPathZip = "C:\\Temp\\unpacked.zip";

        public AgentService()
        {
            InitializeComponent();

            // Logging Set up
            if (!EventLog.SourceExists("AutoDetoursSource"))
            {
                EventLog.CreateEventSource(
                    "AutoDetoursSource", "AutoDetoursLog");
            }
            eventLog.Source = "AutoDetoursSource";
            eventLog.Log = "AutoDetoursLog";
        }

        protected override void OnStart(string[] args)
        {
            // Set up a timer
            timer_agent.Interval = 10000;
            timer_agent.Elapsed += new ElapsedEventHandler(OnTimerCheckAgent);
            timer_agent.Start();

            // Easier to edit a txt file than rebuild all the agent
            string path = @"C:\Temp\agent\api.txt";
            if (File.Exists(path))
            {
                apiBaseURL = File.ReadAllText(path).Replace("\r", "").Replace("\n", "");
                if (apiBaseURL.Last() != '/')
                {
                    apiBaseURL += '/';
                }
            }
            else
            {
                apiBaseURL = "http://172.20.0.10/api/"; // Don't change me! Check out api.txt
            }
            eventLog.WriteEntry("AutoDetours API is located at : " + apiBaseURL);

            // Check if it's a VM
            if (File.Exists(@"C:\Temp\agent\vm.txt"))
            {
                isVM = true;
            }

            // Set up HTTP Client
            client.BaseAddress = new Uri(apiBaseURL);
            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            client.Timeout = TimeSpan.FromSeconds(5);

            // Init Worker
            worker.id = "";
            worker.malware = "";

            // Logging
            eventLog.WriteEntry("AutoDetours service started.");
        }

        protected override void OnStop()
        {
            // Logging
            eventLog.WriteEntry("AutoDetours service stopped.");
        }

        private async Task<bool> RegisterWorker()
        {
            eventLog.WriteEntry("Trying to register");
            String uuid = Guid.NewGuid().ToString();
            HttpResponseMessage response = null;
            // Make post request to /workers/
            try
            {
                response = await client.PostAsync(
                    "workers/",
                    new StringContent("{\"id\":\"" + uuid +"\"}", Encoding.UTF8, "application/json")
                );
            }
            catch (Exception)
            {

            }

            // If everything is okay
            if (response != null && response.IsSuccessStatusCode)
            {
                worker.id = uuid;
                eventLog.WriteEntry("Successfully registered as worker : " + worker.id);
                return true;
            }
            eventLog.WriteEntry("Wasn't able to register worker");
            return false;
        }

        private async Task<bool> GetTask()
        {
            eventLog.WriteEntry("Trying to get task");
            HttpResponseMessage response = null;
            // Make get request to /workers/{id}
            try
            {
                response = await client.GetAsync("workers/" + worker.id + "/get_task/");
            }
            catch (Exception)
            {

            }

            // If everything is okay
            if (response != null && response.IsSuccessStatusCode)
            {
                // Get response data as a String
                String resp = await response.Content.ReadAsStringAsync();

                if (!resp.Contains("error"))
                {
                    // Deserialize the JSON to get a Worker obejct
                    workerTask = JsonConvert.DeserializeObject<WorkerTask>(resp);
                    worker.malware = workerTask.malware;

                    eventLog.WriteEntry("Agent is now tasked with sample : " + worker.malware);
                    return true;
                }
            }
            eventLog.WriteEntry("No task available");
            return false;
        }

        private bool DownloadMalware()
        {
            // Compute download URL
            StringBuilder url = new StringBuilder();
            url.Append(apiBaseURL);
            url.Append("malwares/");
            url.Append(worker.malware);
            url.Append("/download/");

            eventLog.WriteEntry("Downloading file at " + url.ToString());

            String filename = null;
            if (workerTask.isDll == false)
                filename = "C:\\Temp\\sample.exe";
            else
                filename = "C:\\Temp\\sample.dll";

            // Download and save sample to C:/Temp/sample.exe
            WebClient downloader = new WebClient();
            try
            {
                downloader.DownloadFile(url.ToString(), filename);
            }
            catch (Exception)
            {
                return false;
            }

            eventLog.WriteEntry("Sample " + worker.malware + "has been downloaded.");
            return true;
        }

        private void StartTracing()
        {
            // Run Syelog Deamon to extract logs from traceapi32
            syelogd.StartInfo.FileName = "C:\\Temp\\syelogd.exe";
            syelogd.StartInfo.Arguments = "/o C:\\Temp\\traces.txt";
            syelogd.Start();

            withdll.StartInfo.FileName = "C:\\Temp\\withdll.exe";

            // We inject Traceapi DLL into the malware process using withdll.exe
            if (workerTask.isDll == false)
                withdll.StartInfo.Arguments = "/d:C:\\Temp\\trcapi32.dll C:\\Temp\\sample.exe";

            // In case of a DLL we use RunDLL32 to launch the DLL
            else
                withdll.StartInfo.Arguments = "/d:C:\\Temp\\trcapi32.dll rundll32.exe C:\\Temp\\sample.dll," + workerTask.exportName;

            withdll.Start();

            eventLog.WriteEntry("Tracing started...");
        }

        private void StopTracing()
        {
            // Stop both processes
            if (!withdll.HasExited)
                withdll.Kill();
            if (!syelogd.HasExited)
                syelogd.Kill();

            eventLog.WriteEntry("Tracing stopped...");
        }

        private void StartUnpacking()
        {
            // Run Syelog Deamon to extract logs from traceapi32
            malunpack.StartInfo.FileName = "C:\\Temp\\mal_unpack.exe";
            malunpack.StartInfo.Arguments = "/exe C:\\Temp\\sample.exe /timeout " + (worker.time * 1000);
            malunpack.Start();

            eventLog.WriteEntry("Unpacking started...");
        }

        private void StopUnpacking()
        {
            // Stop both processes
            if (!malunpack.HasExited)
                malunpack.Kill();

            eventLog.WriteEntry("Unpacking stopped...");
        }

        private void CompressResults()
        {
            tar.StartInfo.FileName = "C:\\Temp\\tar.exe";
            tar.StartInfo.Arguments = "-a -c -f " + defaultPathZip + " sample.exe.out log";
            tar.Start();

            eventLog.WriteEntry("Results compressed.");
        }

        private string ParseResults()
        {
            // Convert the traces.txt to json format
            string inputFilename = "C:\\Temp\\traces.txt";
            string logs = Parser.ParseLogs(inputFilename);
            eventLog.WriteEntry("Json output has been generated.");
            return logs;
        }

        private async Task<bool> SubmitTask(string jsonLogs)
        {
            // Submit JSON results to the API
            string url = "workers/" + worker.id + "/submit_task/";
            HttpResponseMessage response = null;
            try
            {
                response = await client.PostAsync(url,
                        new StringContent(jsonLogs, Encoding.UTF8, "application/json"));
            }
            catch (Exception)
            {

            }

            if (response != null && response.IsSuccessStatusCode)
            {
                eventLog.WriteEntry("Task successfully submitted.");
                return true;
            }
            return false;
        }

        private async Task<bool> SubmitZip(string path)
        {
            // Submit ZIP results to the API
            string url = "workers/" + worker.id + "/submit_task/";
            HttpResponseMessage response = null;
            try
            {
                // TODO : Here send zip (modify app type + update endpoint)
                response = await client.PostAsync(url,
                        new StringContent(jsonLogs, Encoding.UTF8, "application/json"));
            }
            catch (Exception)
            {

            }

            if (response != null && response.IsSuccessStatusCode)
            {
                eventLog.WriteEntry("ZIP successfully submitted.");
                return true;
            }
            return false;
        }

        private async Task<bool> Cleanup()
        {
            // Make a DELETE request to remove the worker
            string url = "workers/" + worker.id + "/";
            HttpResponseMessage response = null;
            try
            {
                response = await client.DeleteAsync(url);
            }
            catch (Exception)
            {

            }

            if (response != null && response.IsSuccessStatusCode)
            {
                eventLog.WriteEntry("Worker cleaned up!");
                return true;
            }
            return false;
        }

        private async Task<bool> HealthCheck()
        {
            HttpResponseMessage response = null;
            try
            {
                response = await client.GetAsync("");
            }
            catch (Exception)
            {

            }

            if (response != null && response.StatusCode == HttpStatusCode.OK)
            {
                eventLog.WriteEntry("The API is available!");
                return true;
            }
            return false;
        }

        private async void OnTimerCheckAgent(object source, ElapsedEventArgs e)
        {
            if (customMutex)
            {
                customMutex = false;
                switch (state)
                {
                    case AgentState.INIT:
                        if (await HealthCheck())
                        {
                            state = AgentState.REGISTER;
                            goto case AgentState.REGISTER;
                        }
                        break;

                    case AgentState.REGISTER:
                        if (await RegisterWorker())
                        {
                            state = AgentState.GET_TASK;
                            goto case AgentState.GET_TASK;
                        }
                        break;

                    case AgentState.GET_TASK:
                        if (await GetTask())
                        {
                            state = AgentState.DOWNLOAD_SAMPLE;
                            goto case AgentState.DOWNLOAD_SAMPLE;
                        }
                        break;

                    case AgentState.DOWNLOAD_SAMPLE:
                        if (DownloadMalware())
                        {
                            state = AgentState.JOB;
                            goto case AgentState.JOB;
                        }
                        break;

                    case AgentState.JOB:
                        if (!worker.isUnpacking)
                            StartTracing();
                        else
                            StartUnpacking();
                        st.Thread.Sleep(workerTask.time * 1000);

                        if (!worker.isUnpacking)
                            StopTracing();
                        else
                            StopUnpacking();
                        state = AgentState.SEND_RESULTS;
                        goto case AgentState.SEND_RESULTS;

                    case AgentState.SEND_RESULTS:
                        string logs;
                        if (!worker.isUnpacking)
                            logs = ParseResults();

                        if ((!worker.isUnpacking && await SubmitTask(logs)) ||
                            (worker.isUnpacking && await SubmitZip(defaultPathZip)))
                        {
                            state = AgentState.CLEANUP;
                            goto case AgentState.CLEANUP;
                        }
                        break;

                    case AgentState.CLEANUP:
                        if (await Cleanup())
                        {
                            state = AgentState.DONE;
                            goto case AgentState.DONE;
                        }
                        break;

                    case AgentState.DONE:
                        if (isVM)
                        {
                            Process.Start("shutdown", "/s /t 0");
                        }
                        else
                        {
                            ServiceController control = new ServiceController(ServiceName);
                            control.Stop();
                        }
                        break;
                }
                customMutex = true;
            }
        }
    }

    enum AgentState
    {
        INIT,
        REGISTER,
        GET_TASK,
        DOWNLOAD_SAMPLE,
        JOB,
        SEND_RESULTS,
        CLEANUP,
        DONE
    }

    public class Worker
    {
        public String id { get; set; }
        public String malware { get; set; }
    }

    public class WorkerTask
    {
        public String malware { get; set; }
        public int time { get; set; }
        public bool isDll { get; set; }
        public bool isUnpacking { get; set; }
        public string exportName { get; set; }
    }
}

