```

BenchmarkDotNet v0.14.0, Ubuntu 24.04.5 LTS (Noble Numbat)
Intel Xeon Processor 2.80GHz, 1 CPU, 4 logical and 4 physical cores
.NET SDK 10.0.402
  [Host]   : .NET 10.0.13 (10.0.1326.50504), X64 RyuJIT AVX-512F+CD+BW+DQ+VL
  ShortRun : .NET 10.0.13 (10.0.1326.50504), X64 RyuJIT AVX-512F+CD+BW+DQ+VL

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  Categories=History  

```
| Method                      | VersionsPerResource | Provider | Mean            | Gen0     | Gen1     | Allocated  |
|---------------------------- |-------------------- |--------- |----------------:|---------:|---------:|-----------:|
| **&#39;Get latest&#39;**                | **100**                 | **InMemory** |        **890.6 ns** |   **0.0620** |        **-** |    **1.05 KB** |
| &#39;Get oldest version&#39;        | 100                 | InMemory |      2,633.2 ns |   0.1106 |        - |    1.87 KB |
| &#39;Get all versions&#39;          | 100                 | InMemory |      8,720.8 ns |   0.3815 |        - |    6.63 KB |
| &#39;Version history&#39;           | 100                 | InMemory |     21,924.0 ns |   1.0986 |   0.0305 |   19.01 KB |
| &#39;Update (append a version)&#39; | 100                 | InMemory |     27,815.0 ns |   1.1597 |   0.5798 |   19.84 KB |
| **&#39;Get latest&#39;**                | **100**                 | **Sqlite**   |     **49,103.1 ns** |   **0.7324** |        **-** |      **14 KB** |
| &#39;Get oldest version&#39;        | 100                 | Sqlite   |     35,025.8 ns |   0.7935 |        - |   14.01 KB |
| &#39;Get all versions&#39;          | 100                 | Sqlite   |  1,112,537.7 ns |  54.6875 |  27.3438 |  986.15 KB |
| &#39;Version history&#39;           | 100                 | Sqlite   |  1,191,020.9 ns |  58.5938 |  31.2500 | 1004.01 KB |
| &#39;Update (append a version)&#39; | 100                 | Sqlite   |    853,369.8 ns |   1.9531 |        - |   64.33 KB |
| **&#39;Get latest&#39;**                | **1000**                | **InMemory** |        **929.2 ns** |   **0.0620** |        **-** |    **1.05 KB** |
| &#39;Get oldest version&#39;        | 1000                | InMemory |     35,900.6 ns |   0.4883 |        - |     8.9 KB |
| &#39;Get all versions&#39;          | 1000                | InMemory |    286,013.2 ns |   2.4414 |        - |   48.82 KB |
| &#39;Version history&#39;           | 1000                | InMemory |    487,109.6 ns |   8.3008 |   0.9766 |  145.59 KB |
| &#39;Update (append a version)&#39; | 1000                | InMemory |     28,409.0 ns |   1.1597 |   0.5493 |   19.97 KB |
| **&#39;Get latest&#39;**                | **1000**                | **Sqlite**   |     **43,478.9 ns** |   **0.7935** |        **-** |   **14.01 KB** |
| &#39;Get oldest version&#39;        | 1000                | Sqlite   |     30,236.4 ns |   0.7935 |        - |   14.01 KB |
| &#39;Get all versions&#39;          | 1000                | Sqlite   | 11,858,776.6 ns | 578.1250 | 562.5000 |  9831.5 KB |
| &#39;Version history&#39;           | 1000                | Sqlite   | 12,875,329.9 ns | 578.1250 | 531.2500 | 9933.81 KB |
| &#39;Update (append a version)&#39; | 1000                | Sqlite   |    770,044.4 ns |   1.9531 |        - |   64.36 KB |
