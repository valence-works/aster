```

BenchmarkDotNet v0.14.0, Ubuntu 24.04.5 LTS (Noble Numbat)
Intel Xeon Processor 2.80GHz, 1 CPU, 4 logical and 4 physical cores
.NET SDK 10.0.402
  [Host]   : .NET 10.0.13 (10.0.1326.50504), X64 RyuJIT AVX-512F+CD+BW+DQ+VL
  ShortRun : .NET 10.0.13 (10.0.1326.50504), X64 RyuJIT AVX-512F+CD+BW+DQ+VL

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Type                  | Method                               | Categories  | Versions | Provider | Mean           | Gen0     | Gen1     | Allocated   |
|---------------------- |------------------------------------- |------------ |--------- |--------- |---------------:|---------:|---------:|------------:|
| **PortabilityBenchmarks** | **&#39;Export (100 resources)&#39;**             | **Portability** | **10000**    | **InMemory** |     **822.482 μs** |  **18.5547** |   **1.9531** |   **323.31 KB** |
| PortabilityBenchmarks | &#39;Import preview (100 resources)&#39;     | Portability | 10000    | InMemory |   6,504.260 μs | 304.6875 |  93.7500 |  5161.69 KB |
| **PortabilityBenchmarks** | **&#39;Export (100 resources)&#39;**             | **Portability** | **10000**    | **Sqlite**   |   **6,258.084 μs** | **296.8750** | **250.0000** |  **5106.54 KB** |
| PortabilityBenchmarks | &#39;Import preview (100 resources)&#39;     | Portability | 10000    | Sqlite   | 144,416.157 μs | 500.0000 | 250.0000 | 10119.23 KB |
| **QueryBenchmarks**       | **&#39;Query: metadata (ResourceId)&#39;**       | **Queries**     | **10000**    | **InMemory** |     **640.446 μs** |  **10.7422** |   **0.9766** |   **181.53 KB** |
| QueryBenchmarks       | &#39;Query: scalar facet (Equals)&#39;       | Queries     | 10000    | InMemory |     306.333 μs |   3.9063 |        - |    66.23 KB |
| QueryBenchmarks       | &#39;Query: scalar facet (Range)&#39;        | Queries     | 10000    | InMemory |     336.799 μs |   3.9063 |   0.4883 |    66.47 KB |
| QueryBenchmarks       | &#39;Query: array facet (ArrayContains)&#39; | Queries     | 10000    | InMemory |   2,214.036 μs |  35.1563 |   3.9063 |   648.88 KB |
| QueryBenchmarks       | &#39;Reverse reference lookup&#39;           | Queries     | 10000    | InMemory |   1,379.758 μs |  19.5313 |   1.9531 |   348.99 KB |
| **QueryBenchmarks**       | **&#39;Query: metadata (ResourceId)&#39;**       | **Queries**     | **10000**    | **Sqlite**   |   **9,954.118 μs** | **187.5000** |        **-** |  **3303.29 KB** |
| QueryBenchmarks       | &#39;Query: scalar facet (Equals)&#39;       | Queries     | 10000    | Sqlite   |  15,729.472 μs |  31.2500 |        - |   769.02 KB |
| QueryBenchmarks       | &#39;Query: scalar facet (Range)&#39;        | Queries     | 10000    | Sqlite   |  25,310.288 μs |  31.2500 |        - |   604.64 KB |
| QueryBenchmarks       | &#39;Query: array facet (ArrayContains)&#39; | Queries     | 10000    | Sqlite   | 137,809.859 μs | 250.0000 |        - |  4870.37 KB |
| QueryBenchmarks       | &#39;Reverse reference lookup&#39;           | Queries     | 10000    | Sqlite   | 157,384.054 μs | 250.0000 |        - |   6406.8 KB |
| **ResourceBenchmarks**    | **Create**                               | **Resources**   | **10000**    | **InMemory** |      **25.248 μs** |   **0.6409** |   **0.2136** |    **11.15 KB** |
| ResourceBenchmarks    | &#39;Update (append a version)&#39;          | Resources   | 10000    | InMemory |      41.448 μs |   1.1597 |   0.3052 |    19.67 KB |
| ResourceBenchmarks    | Activate                             | Resources   | 10000    | InMemory |       6.411 μs |   0.1602 |   0.0763 |      2.8 KB |
| ResourceBenchmarks    | &#39;Get latest&#39;                         | Resources   | 10000    | InMemory |       2.483 μs |   0.0916 |        - |     1.55 KB |
| ResourceBenchmarks    | &#39;Get active&#39;                         | Resources   | 10000    | InMemory |       2.643 μs |   0.0954 |        - |     1.62 KB |
| **ResourceBenchmarks**    | **Create**                               | **Resources**   | **10000**    | **Sqlite**   |   **5,624.914 μs** |        **-** |        **-** |    **42.86 KB** |
| ResourceBenchmarks    | &#39;Update (append a version)&#39;          | Resources   | 10000    | Sqlite   |   4,800.001 μs |   7.8125 |        - |   139.96 KB |
| ResourceBenchmarks    | Activate                             | Resources   | 10000    | Sqlite   |     514.760 μs |   2.9297 |        - |    53.93 KB |
| ResourceBenchmarks    | &#39;Get latest&#39;                         | Resources   | 10000    | Sqlite   |      75.063 μs |   2.9297 |   0.1221 |     50.5 KB |
| ResourceBenchmarks    | &#39;Get active&#39;                         | Resources   | 10000    | Sqlite   |      91.650 μs |   3.1738 |   0.1221 |     55.5 KB |
