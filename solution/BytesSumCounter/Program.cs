using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Xml.Linq;

namespace BytesSumCounter
{
    class Program
    {
        static void Main(string[] args)
        {            
            //Get available memory
            PerformanceCounter ramCounter = new PerformanceCounter("Memory", "Available MBytes");
            Console.WriteLine("Available memory: " + ramCounter.NextValue() + "Мб");

            //Get number of processors
            int procCount = Environment.ProcessorCount;

            //Calculate the max count memory for one thread
            var MaxMemorySize = (long)ramCounter.NextValue() * (1000000 / 4) / procCount; // In bytes...  
            if(MaxMemorySize > 2147483647)
            {
                MaxMemorySize = 2147483647; // We can read using int in metod stream.Read
            }

            // Check an input directory and store file names from the directory
            string path;
            do
            {
                Console.WriteLine("Введите путь к каталогу с файлами в формате d:/...");
                path = Console.ReadLine();
            }
            while (!Directory.Exists(path));
            string[] allFoundFiles = Directory.GetFiles(path, "*", SearchOption.AllDirectories);

            // 3
            // Divide files on groups:
            // 1. Small size < N
            // 2. Big size > N
            // where N = (Available operating memory / 4)/(available CPUs number)
            List<File4Calc> file4Calcs = new List<File4Calc>();
            Task<ulong>[] tasks = new Task<ulong>[allFoundFiles.Length];

            Semaphore globalSem = new Semaphore(procCount, procCount);
            for (var i = 0; i < allFoundFiles.Length; i++)
            {
                string name = allFoundFiles[i];
                FileInfo filInfo = new FileInfo(name);
                long fileSize = filInfo.Length;
                file4Calcs.Add(new File4Calc() { Name = allFoundFiles[i], Size = fileSize });

                ulong sum = 0;
                if (file4Calcs[i].Size > MaxMemorySize)
                {
                    tasks[i] = Task<ulong>.Factory.StartNew(() =>
                    {
                        int partsCount = (int)((fileSize + MaxMemorySize - 1) / MaxMemorySize); // round to a greater value
                        long partSize = (fileSize / partsCount + 1); // In case if finfo.Length % partsCount != 0
                        Task<ulong>[] tasks4bigfiles = new Task<ulong>[partsCount];
                        Semaphore sem = new Semaphore(1, 1);
                        for (int k = 0; k < partsCount; k++)
                        {
                            long offset = k * partSize;
                            int curPart = k + 1;
                            tasks4bigfiles[k] = Task<ulong>.Factory.StartNew(() =>
                            {
                                globalSem.WaitOne();
                                // In case if (offset + partsCount) > fileSize => rest unread bytes will be 0
                                sem.WaitOne(); // Let's read parts one by one
                                byte[] bytes = new byte[partSize];
                                Console.WriteLine("Started {0}, part {1}/{2}, size {3}", name, curPart, partsCount, fileSize);
                                FileStream f = File.OpenRead(name);
                                f.Position = offset;
                                f.Read(bytes, 0, (int)partSize);
                                f.Close();
                                sem.Release();

                                ulong sumOfPart = 0;
                                for (int j = 0; j < bytes.Length; j++)
                                {
                                    sumOfPart += bytes[j];
                                }
                                Console.WriteLine("Complited {0}, part {1}/{2}, size {3}, sum={4}", name, curPart, partsCount, fileSize, sumOfPart);
                                globalSem.Release();
                                return sumOfPart;
                            });
                        }
                        Task.WaitAll(tasks4bigfiles);
                        foreach (var t in tasks4bigfiles)
                        {
                            sum += t.Result;
                        }
                        return sum;
                    });

                }
                else
                {
                    tasks[i] = Task<ulong>.Factory.StartNew(() =>
                    {
                        globalSem.WaitOne();
                        Console.WriteLine("Started {0}, size {1}", name, fileSize);
                        byte[] bytes = File.ReadAllBytes(name);
                        for (int j = 0; j < bytes.Length; j++)
                        {
                            sum += bytes[j];
                        }
                        Console.WriteLine("Completed {0}, size {1}, sum={2}", name, fileSize, sum);
                        globalSem.Release();
                        return sum;
                    });
                }
                file4Calcs[i].task = tasks[i];
            }
            // Await all tasks finished
            Task.WaitAll(tasks);

            // 6
            // Write result to XML-file
            {
                XDocument xdoc = new XDocument();
                XElement files = new XElement("files");
                foreach (var f in file4Calcs)
                {
                    XElement file = new XElement("file");
                    XAttribute fileNameAttr = new XAttribute("name", f.Name);
                    XElement fileSizeElem = new XElement("bytesSum", f.task.Result);
                    file.Add(fileNameAttr);
                    file.Add(fileSizeElem);
                    files.Add(file);
                }
                xdoc.Add(files);
                xdoc.Save(path + "/InfoAboutFiles.xml");
            }
        }
    }
}
