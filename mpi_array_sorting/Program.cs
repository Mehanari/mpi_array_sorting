// See https://aka.ms/new-console-template for more information
using System.Diagnostics;
using MPI;

internal class Program
{
    public static void Main(string[] args)
    {
        int arraySize = args.Length > 0 ? int.Parse(args[0]) : 100000;
        bool showArrays = args.Length > 1 && bool.Parse(args[1]);
        bool calculateSingleThreadedTime = args.Length > 2 && bool.Parse(args[2]);
        MPI.Environment.Run(ref args, oldComm =>
        {
            //Variables for tracking timespans of different parts of the program
            long scatteringTicks = 0;
            long sortingTicks = 0;
            long communicationTicks = 0;
            long gatheringTicks = 0;
            long totalTicks = 0;
            
            //Initializing the array
            if (oldComm.Rank == 0)
            {
                Console.WriteLine($"Array size: {arraySize}");
            }
            int[] array = new int[arraySize];
            Random random = new Random();
            for (int i = 0; i < arraySize; i++)
            {
                array[i] = random.Next(0, 100000);
            }
            if (oldComm.Rank == 0 && showArrays)
            {
                Console.WriteLine($"Initial array: {string.Join(", ", array)}");
            }
            
            //Setting up the stopwatch
            Stopwatch mainStopwatch = new Stopwatch();
            Stopwatch auxStopwatch = new Stopwatch();
            
            //Setting up the grid topology
            int nodes = oldComm.Size;
            int gridWidth = (int)Math.Sqrt(nodes);
            int gridHeight = nodes / gridWidth;
            int[] dims = {gridWidth, gridHeight};
            if (oldComm.Rank == 0)
            {
                //Console.WriteLine($"Constructing a {dims[0]}x{dims[1]} grid topology");
            }
            var comm = new CartesianCommunicator(oldComm, dims.Length, dims, new []{false, false}, false);
            
            //Virtual linear topology. Use this array to get ranks of the neighbor processes.
            //For example, if you want to get the rank of the process to the right of the current process, you should find your rank
            //in the virtualLine and get the next element in the array.
            //Similarly, if you want to get the rank of the process to the left of the current process, you should get previous element in the array.
            var virtualLine = new int[nodes];
            for (int i = 0; i < dims[0]; i++)
            {
                if (i%2 == 0)
                {
                    for (int j = 0; j < dims[1]; j++)
                    {
                        virtualLine[i * dims[1] + j] = i * dims[1] + j;
                    }
                }
                else
                {
                    for (int j = dims[1] - 1; j >= 0; j--)
                    {
                        virtualLine[i * dims[1] + (dims[1] - 1 - j)] = i * dims[1] + j;
                    }
                }
            }
            
            
            int[] subArray = null;
            int subArraySize = arraySize/comm.Size;
            int remainder = arraySize % comm.Size;
            
            if (comm.Rank == 0)
            {
                auxStopwatch.Start();
                mainStopwatch.Start();
                int[][] subArrays = new int[comm.Size][];
                for (int i = 0; i < comm.Size; i++)
                {
                    subArrays[i] = new int[subArraySize + (i == 0 ? remainder : 0)];
                    Array.Copy(array, i * subArraySize, subArrays[i], 0, subArrays[i].Length);
                }
                subArray = comm.Scatter<int[]>(subArrays, 0);
                //Console.WriteLine($"Process {comm.Rank} received subarray: {string.Join(", ", subArray)}");
            }
            else
            {
                subArray = comm.Scatter<int[]>(0);
                //Console.WriteLine($"Process {comm.Rank} received subarray: {string.Join(", ", subArray)}");
            }

            if (comm.Rank == 0)
            {
                auxStopwatch.Stop();
                scatteringTicks = auxStopwatch.ElapsedTicks;
                TimeSpan scatteringTime = auxStopwatch.Elapsed;
                Console.WriteLine($"Scattering took {scatteringTime.TotalMilliseconds:0.000} ms");
            }

            //Console.WriteLine($"Coordinates of process {comm.Rank}: {string.Join(", ", comm.Coordinates)}");
            if (comm.Rank == 0)
            {
                //Console.WriteLine($"Virtual line: {string.Join(", ", virtualLine)}");    
            }

            if (comm.Rank == 0)
            {
                auxStopwatch.Restart();
            }
            BubbleSort(subArray);
            //Console.WriteLine($"Process {comm.Rank} sorted subarray: {string.Join(", ", subArray)}");
            comm.Barrier(); // Wait for all processes to finish sorting
            if (comm.Rank == 0)
            {
                auxStopwatch.Stop();
                sortingTicks = auxStopwatch.ElapsedTicks;
                TimeSpan sortingTime = auxStopwatch.Elapsed;
                Console.WriteLine($"Sorting took {sortingTime.TotalMilliseconds:0.000} ms");
            }
            
            //The communication phase
            if (comm.Rank == 0)
            {
                auxStopwatch.Restart();
            }
            bool sendingForward = true;
            int exchangesCount = comm.Size - 1 + comm.Size % 2;
            int thisProcessIndex = Array.IndexOf(virtualLine, comm.Rank);
            for (int i = 0; i < exchangesCount; i++)
            {
                if (thisProcessIndex % 2 == 0)
                {
                    //When sending forward even processes should send to the next process in the virtual line and then receive from it
                    if (sendingForward && thisProcessIndex < virtualLine.Length - 1)
                    {
                        int nextProcessIndex = thisProcessIndex + 1;
                        int nextProcessRank = virtualLine[nextProcessIndex];
                        //Console.WriteLine("Communicator with rank {0} will send to and receive from {1}", comm.Rank, nextProcessRank);
                        int[] receivedArray = null;
                        comm.SendReceive<int[]>(subArray, nextProcessRank, 0, nextProcessRank, 0, out receivedArray);
                        //Merge the received array with the current subarray
                        subArray = Merge(subArray, receivedArray);
                        int elementsToKeep = subArraySize + (comm.Rank == 0 ? remainder : 0);
                        subArray = GetPartFromStart(subArray, elementsToKeep);
                        //Console.WriteLine("Process {0} new subarray: {1}", comm.Rank, string.Join(", ", subArray));
                        sendingForward = false;
                        comm.Barrier();
                    }
                    //When sending backward even should send to the previous process in the virtual line and then receive from it
                    else if (!sendingForward && thisProcessIndex > 0)
                    {
                        int previousProcessIndex = thisProcessIndex - 1;
                        int previousProcessRank = virtualLine[previousProcessIndex];
                        //Console.WriteLine("Communicator with rank {0} will send to and receive from {1}", comm.Rank, previousProcessRank);
                        int[] receivedArray = null;
                        comm.SendReceive<int[]>(subArray, previousProcessRank, 0, previousProcessRank, 0, out receivedArray);
                        //Merge the received array with the current subarray
                        subArray = Merge(subArray, receivedArray);
                        int elementsToKeep = subArraySize + (comm.Rank == 0 ? remainder : 0);
                        subArray = GetPartFromEnd(subArray, elementsToKeep);
                        //Console.WriteLine("Process {0} new subarray: {1}", comm.Rank, string.Join(", ", subArray));
                        sendingForward = true;
                        comm.Barrier();
                    }
                    else
                    {
                        sendingForward = !sendingForward;
                        comm.Barrier();
                    }
                }
                else
                {
                    //When sending forward odd processes should receive from the previous process in the virtual line and then send to it
                    if (sendingForward && thisProcessIndex > 0)
                    {
                        int previousProcessIndex = thisProcessIndex - 1;
                        int previousProcessRank = virtualLine[previousProcessIndex];
                        //Console.WriteLine("Communicator with rank {0} will send to and receive from {1}", comm.Rank, previousProcessRank);
                        int[] receivedArray = null;
                        comm.SendReceive<int[]>(subArray, previousProcessRank, 0, previousProcessRank, 0, out receivedArray);
                        //Merge the received array with the current subarray
                        subArray = Merge(subArray, receivedArray);
                        int elementsToKeep = subArraySize + (comm.Rank == 0 ? remainder : 0);
                        subArray = GetPartFromEnd(subArray, elementsToKeep);
                        //Console.WriteLine("Process {0} new subarray: {1}", comm.Rank, string.Join(", ", subArray));
                        sendingForward = false;
                        comm.Barrier();
                    }
                    //When sending backward odd processes should receive from the next process in the virtual line and then send to it
                    else if (!sendingForward && thisProcessIndex < virtualLine.Length - 1)
                    {
                        int nextProcessIndex = thisProcessIndex + 1;
                        int nextProcessRank = virtualLine[nextProcessIndex];
                        //Console.WriteLine("Communicator with rank {0} will send to and receive from {1}", comm.Rank, nextProcessRank);
                        int[] receivedArray = null;
                        comm.SendReceive<int[]>(subArray, nextProcessRank, 0, nextProcessRank, 0, out receivedArray);
                        //Merge the received array with the current subarray
                        subArray = Merge(subArray, receivedArray);
                        int elementsToKeep = subArraySize + (comm.Rank == 0 ? remainder : 0);
                        subArray = GetPartFromStart(subArray, elementsToKeep);
                        //Console.WriteLine("Process {0} new subarray: {1}", comm.Rank, string.Join(", ", subArray));
                        sendingForward = true;
                        comm.Barrier();
                    }
                    else
                    {
                        sendingForward = !sendingForward;
                        comm.Barrier();
                    }
                }
            }
            if (comm.Rank == 0)
            {
                auxStopwatch.Stop();
                communicationTicks = auxStopwatch.ElapsedTicks;
                TimeSpan communicationTime = auxStopwatch.Elapsed;
                Console.WriteLine($"Communication took {communicationTime.TotalMilliseconds:0.000} ms");
            }


            //The gathering phase
            if (comm.Rank == 0)
            {
                auxStopwatch.Restart();
            }
            if (thisProcessIndex == virtualLine.Length - 1)
            {
                int previousProcessIndex = thisProcessIndex - 1;
                int previousProcessRank = virtualLine[previousProcessIndex];
                comm.Send<int[]>(subArray, previousProcessRank, 0);
            }
            else if (thisProcessIndex != 0)
            {
                int nextProcessIndex = thisProcessIndex + 1;
                int nextProcessRank = virtualLine[nextProcessIndex];
                int previousProcessIndex = thisProcessIndex - 1;
                int previousProcessRank = virtualLine[previousProcessIndex];
                int[] receivedArray = comm.Receive<int[]>(nextProcessRank, 0);
                int[] concatenatedArray = Concat(subArray, receivedArray);
                comm.Send<int[]>(concatenatedArray, previousProcessRank, 0);
            }
            else
            {
                int nextProcessIndex = 1;
                int nextProcessRank = virtualLine[nextProcessIndex];
                int[] receivedArray = comm.Receive<int[]>(nextProcessRank, 0);
                int[] result = Concat(subArray, receivedArray);
                auxStopwatch.Stop();
                gatheringTicks = auxStopwatch.ElapsedTicks;
                TimeSpan gatheringTime = auxStopwatch.Elapsed;
                Console.WriteLine($"Gathering took {gatheringTime.TotalMilliseconds:0.000} ms");
                mainStopwatch.Stop();
                totalTicks = mainStopwatch.ElapsedTicks;
                TimeSpan totalTime = mainStopwatch.Elapsed;
                Console.WriteLine($"Total sorting time: {totalTime.TotalMilliseconds:0.000} ms");
                float scatteringPercentage = (float)scatteringTicks * 100 / totalTicks;
                float sortingPercentage = (float)sortingTicks * 100 / totalTicks;
                float communicationPercentage = (float)communicationTicks * 100 / totalTicks;
                float gatheringPercentage = (float)gatheringTicks * 100 / totalTicks;
                //Write with three decimal places
                Console.WriteLine($"Scattering percentage: {scatteringPercentage:0.000}%");
                Console.WriteLine($"Sorting percentage: {sortingPercentage:0.000}%");
                Console.WriteLine($"Communication percentage: {communicationPercentage:0.000}%");
                Console.WriteLine($"Gathering percentage: {gatheringPercentage:0.000}%");
                if (showArrays)
                {
                    Console.WriteLine($"Final sorted array: {string.Join(", ", result)}");
                }

                if (calculateSingleThreadedTime)
                {
                    auxStopwatch.Restart();
                    BubbleSort(array);
                    auxStopwatch.Stop();
                    TimeSpan singleThreadedTime = auxStopwatch.Elapsed;
                    Console.WriteLine($"Single-threaded sorting took {singleThreadedTime.TotalMilliseconds} ms");
                }
            }
        });
    }
    
    private static int[] Concat(int[] array1, int[] array2)
    {
        int[] result = new int[array1.Length + array2.Length];
        Array.Copy(array1, 0, result, 0, array1.Length);
        Array.Copy(array2, 0, result, array1.Length, array2.Length);
        return result;
    }
    
    private static int[] GetPartFromEnd(int[] array, int count)
    {
        int[] result = new int[count];
        Array.Copy(array, array.Length - count, result, 0, count);
        return result;
    }
    
    private static int[] GetPartFromStart(int[] array, int count)
    {
        int[] result = new int[count];
        Array.Copy(array, 0, result, 0, count);
        return result;
    }
    
    private static void BubbleSort(int[] array)
    {
        for (int i = 0; i < array.Length; i++)
        {
            for (int j = 0; j < array.Length - 1; j++)
            {
                if (array[j] > array[j + 1])
                {
                    int temp = array[j];
                    array[j] = array[j + 1];
                    array[j + 1] = temp;
                }
            }
        }
    }
    
    private static int[] Merge(int[] array1, int[] array2)
    {
        int[] result = new int[array1.Length + array2.Length]; //2 operations
        int i = 0, j = 0, k = 0; //3 operations
        
        //One iteration: 2 + 1 + 2 + 2 = 7 operations
        while (i < array1.Length && j < array2.Length) //2 operations
        {
            if (array1[i] < array2[j]) //1 operation
            {
                result[k++] = array1[i++]; //2 operations
            }
            else
            {
                result[k++] = array2[j++]; //2 operations
            }
        }
        
        //One iteration: 2 + 1 = 3 operations
        while (i < array1.Length) //1 operation
        {
            result[k++] = array1[i++]; //2 operations
        }
        
        //One iteration: 2 + 1 = 3 operations
        while (j < array2.Length) //1 operation
        {
            result[k++] = array2[j++]; //2 operations
        }
        
        //Total: 5 + 7n + 3n + 3n = 13n + 5 operations where n is the sum of the lengths of the two arrays
        return result;
    }
}