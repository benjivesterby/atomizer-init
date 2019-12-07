using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Docker.DotNet;
using Docker.DotNet.Models;
using System.IO;

namespace AtomizerStartup
{
    class Program
    {
        private const string _DockerProcess = "Docker Desktop";
        private const string _DockerError = "Docker Desktop is not running.  Please start it and hit enter to continue...";
        static ConsoleSpiner spin = new ConsoleSpiner();
        


        static void Main(string[] args)
        {
            MainAsync().Wait();
        }

        //Main Async Task -------------------------------------------------------------------------------------------------
        static async Task MainAsync()
        {

            /* **************************************************************************************
             * Make Sure Docker Desktop is running
             * (Requirement 4.3.1)
             */
            Process[] processes = Process.GetProcessesByName(_DockerProcess);

            while (processes.Length == 0)
            {
                Console.WriteLine(_DockerError);
                Console.ReadLine();
                processes = Process.GetProcessesByName(_DockerProcess);
            }

            Console.WriteLine("[Docker] Desktop Started");

            /* 
             * While Docker Desktop is not responsive, show a spinner
             * (Requirement 4.3.2)
             */
            if (!IsDockerResponsive())
            {
                ConsoleSpiner spin = new ConsoleSpiner();
                Console.Write("[Docker] Waiting for Docker Desktop Response....");
                while (!IsDockerResponsive())
                {
                    spin.Turn();
                };
                Console.WriteLine("\n[Docker] Desktop Responsive");
            }

            /* 
             * Run Docker Containers
             * execute (Requirement 4.3.3)
             */
            await RunContainers();
        }

        /* **************************************************************************************
         * Make sure Docker Desktop is responding
         * (If it was just started it will take a minute to respond)
         * (Requirement 4.3.2.1)
         */
        public static bool IsDockerResponsive()
        {
            try
            {
                System.Diagnostics.ProcessStartInfo procStartInfo =
                    new System.Diagnostics.ProcessStartInfo("cmd", "/c " + "docker ps");

                procStartInfo.RedirectStandardOutput = true;
                procStartInfo.UseShellExecute = false;
                procStartInfo.CreateNoWindow = true;
                System.Diagnostics.Process proc = new System.Diagnostics.Process();
                proc.StartInfo = procStartInfo;
                proc.Start();
                // Get the output into a string
                string result = proc.StandardOutput.ReadToEnd();

                //Test if result contains the string "CONTAINER", return true if it does
                //return false if it doens't, that means it is not responsive yet.
                return result.Contains("CONTAINER");
            }
            catch (Exception e)
            {
                throw e;
            }
        }

        /* **************************************************************************************
         * Run Docker Containers
         * (Requirement 4.3.3)
         */
        public static async Task RunContainers()
        {
            String cmdCreateNetwork = "docker network create atomizer_nw";
            String cmdRabbitMQ = "docker run -d --hostname my-rabbit --restart always --name some-rabbit -p 8080:15672 --network=atomizer_nw rabbitmq:3-management";
            String cmdPullAtomizer = "docker pull benjivesterby/atomizer:latest";
            String cmdAtomizer = "docker run -e CONNECTIONSTRING=amqp://guest:guest@some-rabbit:5672/ -e QUEUE=atomizer --network=atomizer_nw benjivesterby/atomizer:latest";
            String cmdAtomizerUI = "docker run -d --hostname my-atomizer-ui --restart always --name atomizer-ui -p 3000:3000 --network=atomizer_nw -e RABBIT_NAME=some-rabbit -e QUEUE=atomizer -e APPID=0f7827a0-0baa-11ea-a738-a707993eaee1 benjivesterby/atomizer-test-ui:latest";
            String cmdAtomizerURL = "start http://localhost:3000";
            int exitCode;



            /* 
             * Create Docker Bridge Netwwork
             * (Requirement 4.3.3.1)
             */
            Console.WriteLine("[Docker] Create Atomizer Bridge Network");
            do
            {
                exitCode = ExecuteCommandSync(cmdCreateNetwork, true, "already exists");
            } while (exitCode != 0);
            Console.WriteLine("[Docker] Atomizer Bridge Network Created");



            /* 
             * Run RabbitMQ Docker Container
             * (Requirement 4.3.3.2)
             */
            Console.WriteLine("[RabbitMQ] Starting Container");
            do
            {
                exitCode = ExecuteCommandSync(cmdRabbitMQ, true, "already in use");
            } while (exitCode != 0);
            Console.WriteLine("[RabbitMQ] Container Started");



            /* 
             * Verify that RabbitMQ is Responsive
             * (Requirement 4.3.3.3)
             */
            Console.Write("[RabbitMQ] Waiting for Response....");
            System.Net.HttpStatusCode rabbitStatus = System.Net.HttpStatusCode.NotFound;
            do
            {
                try
                {
                    spin.Turn();
                    using (var client = new HttpClient())
                    {
                        HttpResponseMessage result = await client.GetAsync("http://localhost:8080");
                        rabbitStatus = result.StatusCode;
                    }
                }
                catch (Exception)
                {
                    //Sleep for a second then try again
                    Thread.Sleep(1000);
                    spin.Turn();
                }

            } while (rabbitStatus != System.Net.HttpStatusCode.OK);

            Console.WriteLine("\n[RabbitMQ] Responsive");


            /* 
             * Run three instances of Atomizer
             * (Requirement 4.3.3.4)
             */
            int maxInstances = 3;
            int instanceCount = 0;

            Boolean hasAtomizerImage = false;

            DockerClient dockerClient = new DockerClientConfiguration(
            new Uri("npipe://./pipe/docker_engine"))
            .CreateClient();

            IList<ImagesListResponse> imageList = await dockerClient.Images.ListImagesAsync(new ImagesListParameters());


            //See if Atomizer image exists already
            Console.WriteLine("[Atomizer] Checking for image");
            foreach (ImagesListResponse image in imageList)
            {

                foreach(string tag in image.RepoTags)
                {
                    //Console.WriteLine("IMAGE TAG: {0}", tag);
                    if (tag.Contains("benjivesterby/atomizer:latest"))
                    {
                        hasAtomizerImage = true;
                        Console.WriteLine("[Atomizer] Image already exists");
                        break;
                    }
                }
                if (hasAtomizerImage) break;
            }

            //If image does not exist yet, pull it down
            if (!hasAtomizerImage)
            {
                Console.WriteLine("[Atomizer] Image does not exist yet.  Pulling latest image...");
                do
                {
                    exitCode = ExecuteCommandSync(cmdPullAtomizer, true, null);
                } while (exitCode != 0);
            }


            //Start 3 instances of the atomizer and make sure they are running.
            Console.WriteLine("[Atomizer] Starting Container Instances...");
            do
            {
                //Lookup how many atomizer containders are running.
                //Console.WriteLine("IMAGE LIST");
                instanceCount = 0;                
                IList<ContainerListResponse> containers = await dockerClient.Containers.ListContainersAsync(
                new ContainersListParameters());

                foreach (ContainerListResponse c in containers)
                {
                    //Console.WriteLine("Container: {0} STATUS {1}", c.Image, c.Status);
                    if (c.Image.Contains("atomizer"))
                    {
                        if (c.Status.Contains("Up"))
                        {
                            instanceCount += 1;
                        }
                    }
                }

                if (instanceCount < maxInstances)
                {
                    AtomizerCommandSync(cmdAtomizer);
                    Thread.Sleep(5000);
                } 

            } while (instanceCount < maxInstances);

            Console.WriteLine("[Atomizer] {0} Containers Started", instanceCount);


            /* 
             * Run Atomizer UI Container
             * (Requirement 4.3.3.5)
             */
            Console.WriteLine("[Atomizer UI] Starting Container");
            do
            {
                exitCode = ExecuteCommandSync(cmdAtomizerUI, true, "already in use");
            } while (exitCode != 0);
            Console.WriteLine("[Atomizer UI] Container Started");



            /* 
             * Run Atomizer UI Container
             * (Requirement 4.3.3.6)
             */
            Console.Write("[Atomizer UI] Waiting for Response....");
            System.Net.HttpStatusCode uiStatus = System.Net.HttpStatusCode.NotFound;
            do
            {
                try
                {
                    spin.Turn();
                    using (var client = new HttpClient())
                    {
                        HttpResponseMessage result = await client.GetAsync("http://localhost:3000");
                        uiStatus = result.StatusCode;
                    }
                }
                catch (Exception)
                {
                    //Sleep for a second then try again
                    Thread.Sleep(1000);
                    spin.Turn();
                }

            } while (uiStatus != System.Net.HttpStatusCode.OK);

            Console.WriteLine("\n[Atomizer UI] Responsive");
            Console.WriteLine("[Atomizer UI] Opening browser to http://localhost:3000");


            /* 
             * Open browser to Atomizer UI homepage
             * (Requirement 4.3.3.7)
             */
            ExecuteCommandSync(cmdAtomizerURL, false, null);


            /* 
             * Acknowledge End of Startup
             * (Requirement 4.3.3.8)
             */
            Console.WriteLine("Atomizer Environment has been started.  Please hit enter to exit.");
            Console.ReadLine();

        }

        //Execute a windows command synchronously --------------------------------------------------------------------------------
        public static int ExecuteCommandSync(string command, bool noWindow, string exitString)
        {
            try
            {

                var p = new Process();

                System.Diagnostics.ProcessStartInfo procStartInfo =
                    new System.Diagnostics.ProcessStartInfo("cmd", "/C " + command);

                procStartInfo.UseShellExecute = false;
                procStartInfo.RedirectStandardOutput = true;
                procStartInfo.RedirectStandardError = true;
                procStartInfo.CreateNoWindow = true;

                p.StartInfo = procStartInfo;

                string error = null;
                p.ErrorDataReceived += new DataReceivedEventHandler((sender, e) =>
                {
                    Console.WriteLine(e.Data);
                    error += e.Data;
                });

                p.Start();

                // To avoid deadlocks, use an asynchronous read operation on at least one of the streams.  
                p.BeginErrorReadLine();
                string output = p.StandardOutput.ReadToEnd();
                p.WaitForExit();

                Console.WriteLine($"[OUTPUT]: {output}");
                //Console.WriteLine($"[ERROR]: {eOut}");

                //Control Known Errors
                if (!String.IsNullOrEmpty(exitString) && error.Contains(exitString))
                {
                    return 0;
                }


                return p.ExitCode;
            }
            catch (Exception objException)
            {
                throw objException;
            }
        }

        public static void AtomizerCommandSync(string command)
        {
            try
            {
                var p = new Process();
                System.Diagnostics.ProcessStartInfo procStartInfo =
                    new System.Diagnostics.ProcessStartInfo("cmd", "/C " + command);
                p.StartInfo = procStartInfo;
                p.Start();
            }
            catch (Exception objException)
            {
                throw objException;
            }
        }


        //Spinner Class - used to show user that an action is processing -----------------------------------------------------
        public class ConsoleSpiner
        {
            int counter;
            public ConsoleSpiner()
            {
                counter = 0;
            }
            public void Turn()
            {
                counter++;
                switch (counter % 4)
                {
                    case 0: Console.Write("/"); break;
                    case 1: Console.Write("-"); break;
                    case 2: Console.Write("\\"); break;
                    case 3: Console.Write("|"); break;
                }
                Console.SetCursorPosition(Console.CursorLeft - 1, Console.CursorTop);
            }
        }


    }
}
