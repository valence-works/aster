```

BenchmarkDotNet v0.14.0, Ubuntu 24.04.5 LTS (Noble Numbat)
Intel Xeon Processor 2.80GHz, 1 CPU, 4 logical and 4 physical cores
.NET SDK 10.0.402
  [Host]   : .NET 10.0.13 (10.0.1326.50504), X64 RyuJIT AVX-512F+CD+BW+DQ+VL
  ShortRun : .NET 10.0.13 (10.0.1326.50504), X64 RyuJIT AVX-512F+CD+BW+DQ+VL

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  Categories=History  

```
| Method                      | VersionsPerResource | Provider | Mean          | Gen0      | Gen1      | Allocated   |
|---------------------------- |-------------------- |--------- |--------------:|----------:|----------:|------------:|
| **&#39;Get latest&#39;**                | **100**                 | **InMemory** |      **9.628 μs** |    **0.3815** |         **-** |     **6.59 KB** |
| &#39;Get oldest version&#39;        | 100                 | InMemory |      8.585 μs |    0.3815 |         - |     6.62 KB |
| &#39;Get all versions&#39;          | 100                 | InMemory |      8.254 μs |    0.3815 |         - |     6.59 KB |
| &#39;Version history&#39;           | 100                 | InMemory |     19.222 μs |    1.0986 |    0.0305 |    18.98 KB |
| &#39;Update (append a version)&#39; | 100                 | InMemory |    545.071 μs |   12.6953 |    2.4414 |   214.72 KB |
| **&#39;Get latest&#39;**                | **100**                 | **Sqlite**   |  **1,136.784 μs** |   **54.6875** |   **27.3438** |   **986.12 KB** |
| &#39;Get oldest version&#39;        | 100                 | Sqlite   |  1,169.156 μs |   54.6875 |   27.3438 |   986.08 KB |
| &#39;Get all versions&#39;          | 100                 | Sqlite   |  1,124.043 μs |   54.6875 |   27.3438 |   986.12 KB |
| &#39;Version history&#39;           | 100                 | Sqlite   |  1,237.324 μs |   58.5938 |   31.2500 |  1004.05 KB |
| &#39;Update (append a version)&#39; | 100                 | Sqlite   |  4,232.667 μs |  187.5000 |  117.1875 |  3173.61 KB |
| **&#39;Get latest&#39;**                | **1000**                | **InMemory** |    **262.929 μs** |    **2.4414** |         **-** |    **48.79 KB** |
| &#39;Get oldest version&#39;        | 1000                | InMemory |    261.809 μs |    2.4414 |         - |    48.81 KB |
| &#39;Get all versions&#39;          | 1000                | InMemory |    255.792 μs |    2.4414 |         - |    48.79 KB |
| &#39;Version history&#39;           | 1000                | InMemory |    515.328 μs |    7.8125 |    0.9766 |   145.56 KB |
| &#39;Update (append a version)&#39; | 1000                | InMemory |    619.060 μs |   10.7422 |    1.9531 |   186.07 KB |
| **&#39;Get latest&#39;**                | **1000**                | **Sqlite**   | **11,417.533 μs** |  **578.1250** |  **562.5000** |  **9831.47 KB** |
| &#39;Get oldest version&#39;        | 1000                | Sqlite   | 12,400.028 μs |  578.1250 |  562.5000 |  9831.49 KB |
| &#39;Get all versions&#39;          | 1000                | Sqlite   | 11,822.658 μs |  578.1250 |  562.5000 |  9831.47 KB |
| &#39;Version history&#39;           | 1000                | Sqlite   | 12,602.865 μs |  578.1250 |  531.2500 |  9933.77 KB |
| &#39;Update (append a version)&#39; | 1000                | Sqlite   | 27,449.538 μs | 1187.5000 | 1062.5000 | 20068.16 KB |
