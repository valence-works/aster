```

BenchmarkDotNet v0.14.0, Ubuntu 24.04.5 LTS (Noble Numbat)
Intel Xeon Processor 2.80GHz, 1 CPU, 4 logical and 4 physical cores
.NET SDK 10.0.402
  [Host]   : .NET 10.0.13 (10.0.1326.50504), X64 RyuJIT AVX-512F+CD+BW+DQ+VL
  ShortRun : .NET 10.0.13 (10.0.1326.50504), X64 RyuJIT AVX-512F+CD+BW+DQ+VL

Job=ShortRun  InvocationCount=64  IterationCount=3  
LaunchCount=1  UnrollFactor=1  WarmupCount=3  
Categories=History  

```
| Method                      | VersionsPerResource | Provider | Mean        | Allocated |
|---------------------------- |-------------------- |--------- |------------:|----------:|
| **&#39;Update (append a version)&#39;** | **100**                 | **InMemory** |    **65.96 μs** |  **19.82 KB** |
| **&#39;Update (append a version)&#39;** | **100**                 | **Sqlite**   | **1,038.57 μs** |  **64.69 KB** |
| **&#39;Update (append a version)&#39;** | **1000**                | **InMemory** |    **83.02 μs** |  **19.82 KB** |
| **&#39;Update (append a version)&#39;** | **1000**                | **Sqlite**   | **1,170.52 μs** |  **64.36 KB** |
