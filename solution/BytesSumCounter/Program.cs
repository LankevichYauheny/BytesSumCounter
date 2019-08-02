using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Linq;
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
            // 1
            // Get command line args
            
            //Get available memory
            PerformanceCounter ramCounter = new PerformanceCounter("Memory", "Available MBytes");
            Console.WriteLine("Available memory: " + ramCounter.NextValue() + "Мб");

            //Get number of processors
            int procCount = Environment.ProcessorCount;
            //Console.WriteLine("The number of processors " + "on this computer is {0}.", procCount);


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
                Console.WriteLine("Введите путь к каталогу с файлами в формате d:/.../...");
                path = Console.ReadLine();
            }
            while (!Directory.Exists(path));
            string[] allFoundFiles = Directory.GetFiles(path, "*", SearchOption.AllDirectories);

            // 3
            // Divide files on groups:
            // 1. Small size < N
            // 2. Big size > N
            // where N = (Available operating memory)/(available CPUs number)
            List<BigFile> bigFiles = new List<BigFile>(); ;
            List<SmallFile> smallFiles = new List<SmallFile>();
            foreach (string f in allFoundFiles)
            {
                FileInfo filInfo = new FileInfo(f);

                if (filInfo.Length > MaxMemorySize)
                {
                    bigFiles.Add(new BigFile() { FileName = f});
                    Console.WriteLine("Big file {0}", f);
                }
                else
                {
                    smallFiles.Add(new SmallFile() { FileName = f });
                    Console.WriteLine("Small file {0}", f);
                }
            }

            // 4
            // Start Tasks to calculate Small size files
            // Start Tasks to calculate Big size files

            // 5
            // Await all tasks finished
            Task<ulong>[] tasks = new Task<ulong>[allFoundFiles.Length];

            for(var i = 0; i < smallFiles.Count; i++)
            {
                string name = smallFiles[i].FileName;
                FileInfo filInfo = new FileInfo(name);
                long fileSize = filInfo.Length;
                //Delegate void calcBytes = 
                tasks[i] = Task<ulong>.Factory.StartNew(() =>
                {
                    Console.WriteLine("Start to read file {0}, size {1}", name, fileSize);
                    byte[] bytes = File.ReadAllBytes(name);
                    ulong sum = 0;
                    for (int j = 0; j < bytes.Length; j++)
                    {
                        sum += bytes[j];
                    }
                    Console.WriteLine("Completed calculation for file {0}, size {1}, sum={2}", name, fileSize, sum);
                    return sum;
                });
            }

            for (int i = 0; i < bigFiles.Count; i++)
            {
                string name = bigFiles[i].FileName;
                tasks[smallFiles.Count + i] = Task<ulong>.Factory.StartNew(() =>
                {
                    FileInfo finfo = new FileInfo(name);
                    long fileSize = finfo.Length;
                    int partsCount = (int)((fileSize + MaxMemorySize - 1) / MaxMemorySize);
                    long partSize = (fileSize / partsCount + 1); // In case if finfo.Length % partsCount != 0
                    Task<ulong>[] tasks4bigfiles = new Task<ulong>[partsCount];
                    ulong sum = 0;
                    Semaphore sem = new Semaphore(1, 1);
                    for (int k = 0; k < partsCount; k++)
                    {
                        long offset = k * partSize;
                        int curPart = k + 1;
                        tasks4bigfiles[k] = Task<ulong>.Factory.StartNew(() =>
                        {
                            sem.WaitOne();
                            FileStream f = File.OpenRead(name);
                            f.Position = offset;
                            // In case if (offset + partsCount) > fileSize => rest unread bytes will be 0
                            byte[] bytes = new byte[partSize];
                            Console.WriteLine("Start to read file {0}, part {1}/{2}, size {3}", name, curPart, partsCount, fileSize);
                            f.Read(bytes, 0, (int)partSize);
                            f.Close();
                            sem.Release();

                            ulong sumOfPart = 0;
                            for (int j = 0; j < bytes.Length; j++)
                            {
                                sumOfPart += bytes[j];
                            }
                            Console.WriteLine("Complited calculation for file {0}, part {1}/{2}, size {3}", name, curPart, partsCount, fileSize);
                            return sumOfPart;
                        });
                    }
                    Task.WaitAll(tasks4bigfiles);
                    foreach(var t in tasks4bigfiles)
                    {
                        sum += t.Result;
                    }
                    return sum;
                });
            }
            Task.WaitAll(tasks);
            // 6
            // Write result to XML-file
            //for (var i = 0; i < allFoundFiles.Length; i++)
            {
                //FileInfo finfo = new FileInfo(allFoundFiles[i]);
                //Console.WriteLine("{0} : size= {1} Sum={2}", allFoundFiles[i], finfo.Length, tasks[i].Result);
                XDocument xdoc = new XDocument();
                XElement files = new XElement("files");
                for (var i = 0; i < smallFiles.Count; i++)
                {
                    XElement file = new XElement("file");
                    XAttribute fileNameAttr = new XAttribute("name", allFoundFiles[i]);
                    XElement fileSizeElem = new XElement("bytesSum", tasks[i].Result);
                    file.Add(fileNameAttr);
                    file.Add(fileSizeElem);
                    files.Add(file);
                }
                for (var i = 0; i < bigFiles.Count; i++)
                {
                    XElement file = new XElement("file");
                    XAttribute fileNameAttr = new XAttribute("name", bigFiles[i].FileName);
                    XElement fileSizeElem = new XElement("bytesSum", tasks[i + smallFiles.Count].Result);
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
