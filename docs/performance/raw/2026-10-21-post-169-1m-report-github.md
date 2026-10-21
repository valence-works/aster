```

BenchmarkDotNet v0.14.0, Ubuntu 24.04.5 LTS (Noble Numbat)
Intel Xeon Processor 2.80GHz, 1 CPU, 4 logical and 4 physical cores
.NET SDK 10.0.402
  [Host]   : .NET 10.0.13 (10.0.1326.50504), X64 RyuJIT AVX-512F+CD+BW+DQ+VL
  ShortRun : .NET 10.0.13 (10.0.1326.50504), X64 RyuJIT AVX-512F+CD+BW+DQ+VL

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  Categories=Resources  

```
| Method                      | Versions | Provider | Mean       | Gen0   | Gen1   | Allocated |
|---------------------------- |--------- |--------- |-----------:|-------:|-------:|----------:|
| **Create**                      | **1000000**  | **InMemory** |  **23.951 μs** | **0.6104** | **0.1831** |  **11.22 KB** |
| &#39;Update (append a version)&#39; | 1000000  | InMemory |  33.966 μs | 0.7324 | 0.1831 |  12.73 KB |
| Activate                    | 1000000  | InMemory |   9.932 μs | 0.1221 | 0.0458 |   2.27 KB |
| &#39;Get latest&#39;                | 1000000  | InMemory |   2.111 μs | 0.0610 |      - |   1.07 KB |
| &#39;Get active&#39;                | 1000000  | InMemory |   3.193 μs | 0.0954 |      - |   1.65 KB |
| **Create**                      | **1000000**  | **Sqlite**   | **791.740 μs** | **1.9531** |      **-** |  **42.12 KB** |
| &#39;Update (append a version)&#39; | 1000000  | Sqlite   | 880.972 μs | 3.9063 |      - |  70.18 KB |
| Activate                    | 1000000  | Sqlite   | 407.854 μs | 0.9766 |      - |  19.17 KB |
| &#39;Get latest&#39;                | 1000000  | Sqlite   |  49.504 μs | 0.9155 |      - |  15.84 KB |
| &#39;Get active&#39;                | 1000000  | Sqlite   |  67.691 μs | 1.2207 |      - |  21.26 KB |
