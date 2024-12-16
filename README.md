# Short description
This is a simple distributed algorithm for sorting an array of integers with MPI. Created as a part of a practical assignment on Technologies of High Productive Computing course in NURE.

# How to build (Windows)
You should have one version of .Net installed.
Build with a command: 
```bat
dotnet build
```

# How to run
Go to the directory with an .exe file of your build, open terminal for this directory and run following command:
```bat
mpiexec -n *number of processors* *exe_file_name.exe* *number of elements in an array* *show arrays or not* *show time for single-threaded variant*
```
Example:
```bat
mpiexec -n 9 mpi_array_sorting.exe 10000 false true
```
Given example will run program with 9 processes. It will generate an array of integers with 10000 elements, it WILL NOT show generated and sorted array, and it will show the time it takes to sort
this array on a single thread in milliseconds.
