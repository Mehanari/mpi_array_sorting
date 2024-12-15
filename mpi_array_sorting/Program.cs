// See https://aka.ms/new-console-template for more information

using System.Text;
using MPI;

internal class Program
{
    public static void Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.InputEncoding = Encoding.UTF8;
        
        MPI.Environment.Run(ref args, communicator =>
        {
            int nodes = communicator.Size;
            int gridWidth = (int)Math.Sqrt(nodes);
            int gridHeight = nodes / gridWidth;
            int[] dims = new int[]{gridWidth, gridHeight};
            CartesianCommunicator.Map(communicator, 2, dims, new bool[]{false, false});

            
            int[] subArray = null;
            if (communicator.Rank == 0)
            {
                Random rand = new Random();
                int[] array = null;
                int arraySize = 100;
                array = Enumerable.Range(0, arraySize).Select(_ => rand.Next(1000)).ToArray();
                int[][] subArrays = new int[communicator.Size][];
                for (int i = 0; i < communicator.Size; i++)
                {
                    int subArraySize = arraySize/communicator.Size;
                    int remainder = arraySize % communicator.Size;
                    subArrays[i] = new int[subArraySize + (i < remainder ? 1 : 0)];
                    Array.Copy(array, i * subArraySize, subArrays[i], 0, subArrays[i].Length);
                }
                subArray = communicator.Scatter<int[]>(subArrays, 0);
                Console.WriteLine($"Process {communicator.Rank} received subarray: {string.Join(", ", subArray)}");
            }
            else
            {
                subArray = communicator.Scatter<int[]>(0);
                Console.WriteLine($"Process {communicator.Rank} received subarray: {string.Join(", ", subArray)}");
            }
            
            BubbleSort(subArray);
            Console.WriteLine($"Process {communicator.Rank} sorted subarray: {string.Join(", ", subArray)}");
            communicator.Barrier(); // Wait for all processes to finish sorting

            bool sendingForward = true;
            int exchangesCount = communicator.Size - 1 + communicator.Size % 2;
            if (communicator.Rank % 2 == 0)
            {
                if (sendingForward && communicator.Rank < communicator.Size - 1)
                {
                    //Send subarray to the next process
                    communicator.Send(subArray, communicator.Rank + 1, 0);
                    //Receive subarray from the next process
                    int[] receivedArray = communicator.Receive<int[]>(communicator.Rank + 1, 0);
                    //Merge the received array with the current subarray
                    subArray = Merge(subArray, receivedArray);
                    Console.WriteLine($"Process {communicator.Rank} received subarray: {string.Join(", ", subArray)}");
                    sendingForward = false;
                }
                else if (communicator.Rank > 0)
                {
                    //Receive subarray from the previous process
                    int[] receivedArray = communicator.Receive<int[]>(communicator.Rank - 1, 0);
                    //Send subarray to the previous process
                    communicator.Send(subArray, communicator.Rank - 1, 0);
                    //Merge the received array with the current subarray
                    subArray = Merge(subArray, receivedArray);
                    Console.WriteLine($"Process {communicator.Rank} received subarray: {string.Join(", ", subArray)}");
                    sendingForward = true;
                }
            }
            else
            {
                if (sendingForward)
                {
                    //Sending to the previous process
                    communicator.Send(subArray, communicator.Rank - 1, 0);
                    //Receiving from the previous process
                    int[] receivedArray = communicator.Receive<int[]>(communicator.Rank - 1, 0);
                    //Merging the received array with the current subarray
                    subArray = Merge(subArray, receivedArray);
                    Console.WriteLine($"Process {communicator.Rank} received subarray: {string.Join(", ", subArray)}");
                    sendingForward = false;
                }
                else if (communicator.Rank < communicator.Size - 1)
                {
                    //Receiving from the next process
                    int[] receivedArray = communicator.Receive<int[]>(communicator.Rank + 1, 0);
                    //Sending to the next process
                    communicator.Send(subArray, communicator.Rank + 1, 0);
                    //Merging the received array with the current subarray
                    subArray = Merge(subArray, receivedArray);
                    Console.WriteLine($"Process {communicator.Rank} received subarray: {string.Join(", ", subArray)}");
                    sendingForward = true;
                }
            }
        });
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