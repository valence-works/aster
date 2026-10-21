```

BenchmarkDotNet v0.14.0, Ubuntu 24.04.5 LTS (Noble Numbat)
Intel Xeon Processor 2.80GHz, 1 CPU, 4 logical and 4 physical cores
.NET SDK 10.0.402
  [Host]   : .NET 10.0.13 (10.0.1326.50504), X64 RyuJIT AVX-512F+CD+BW+DQ+VL
  ShortRun : .NET 10.0.13 (10.0.1326.50504), X64 RyuJIT AVX-512F+CD+BW+DQ+VL

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Type                  | Method                               | Categories  | Versions | Provider | Mean             | Gen0      | Gen1     | Allocated   |
|---------------------- |------------------------------------- |------------ |--------- |--------- |-----------------:|----------:|---------:|------------:|
| **PortabilityBenchmarks** | **&#39;Export (100 resources)&#39;**             | **Portability** | **10000**    | **InMemory** |       **886.721 μs** |   **17.5781** |   **1.9531** |   **323.31 KB** |
| PortabilityBenchmarks | &#39;Import preview (100 resources)&#39;     | Portability | 10000    | InMemory |     6,581.325 μs |  304.6875 |  85.9375 |  5161.59 KB |
| **PortabilityBenchmarks** | **&#39;Export (100 resources)&#39;**             | **Portability** | **10000**    | **Sqlite**   |     **5,984.193 μs** |  **296.8750** | **250.0000** |  **5106.58 KB** |
| PortabilityBenchmarks | &#39;Import preview (100 resources)&#39;     | Portability | 10000    | Sqlite   |   147,576.127 μs |  500.0000 | 250.0000 | 10119.25 KB |
| **PortabilityBenchmarks** | **&#39;Export (100 resources)&#39;**             | **Portability** | **100000**   | **InMemory** |    **12,960.829 μs** |   **15.6250** |        **-** |   **718.83 KB** |
| PortabilityBenchmarks | &#39;Import preview (100 resources)&#39;     | Portability | 100000   | InMemory |     6,131.574 μs |  304.6875 |  93.7500 |  5161.69 KB |
| **PortabilityBenchmarks** | **&#39;Export (100 resources)&#39;**             | **Portability** | **100000**   | **Sqlite**   |     **6,002.316 μs** |  **296.8750** | **250.0000** |  **5106.57 KB** |
| PortabilityBenchmarks | &#39;Import preview (100 resources)&#39;     | Portability | 100000   | Sqlite   | 1,196,206.875 μs |         - |        - | 10120.48 KB |
| **QueryBenchmarks**       | **&#39;Query: metadata (ResourceId)&#39;**       | **Queries**     | **10000**    | **InMemory** |       **754.534 μs** |   **10.7422** |   **0.9766** |   **181.56 KB** |
| QueryBenchmarks       | &#39;Query: scalar facet (Equals)&#39;       | Queries     | 10000    | InMemory |       328.833 μs |    3.9063 |   0.4883 |    66.26 KB |
| QueryBenchmarks       | &#39;Query: scalar facet (Range)&#39;        | Queries     | 10000    | InMemory |       392.683 μs |    3.9063 |   0.4883 |     66.5 KB |
| QueryBenchmarks       | &#39;Query: array facet (ArrayContains)&#39; | Queries     | 10000    | InMemory |     1,633.590 μs |   37.1094 |   3.9063 |   655.82 KB |
| QueryBenchmarks       | &#39;Reverse reference lookup&#39;           | Queries     | 10000    | InMemory |     1,888.146 μs |   19.5313 |        - |   348.95 KB |
| **QueryBenchmarks**       | **&#39;Query: metadata (ResourceId)&#39;**       | **Queries**     | **10000**    | **Sqlite**   |    **10,659.231 μs** |  **187.5000** |        **-** |  **3303.29 KB** |
| QueryBenchmarks       | &#39;Query: scalar facet (Equals)&#39;       | Queries     | 10000    | Sqlite   |    15,847.560 μs |   31.2500 |        - |   769.05 KB |
| QueryBenchmarks       | &#39;Query: scalar facet (Range)&#39;        | Queries     | 10000    | Sqlite   |    28,322.205 μs |   31.2500 |        - |   604.64 KB |
| QueryBenchmarks       | &#39;Query: array facet (ArrayContains)&#39; | Queries     | 10000    | Sqlite   |   132,383.565 μs |  250.0000 |        - |   4870.4 KB |
| QueryBenchmarks       | &#39;Reverse reference lookup&#39;           | Queries     | 10000    | Sqlite   |   139,923.264 μs |  250.0000 |        - |   6406.8 KB |
| **QueryBenchmarks**       | **&#39;Query: metadata (ResourceId)&#39;**       | **Queries**     | **100000**   | **InMemory** |    **34,619.782 μs** |   **71.4286** |        **-** |  **1684.44 KB** |
| QueryBenchmarks       | &#39;Query: scalar facet (Equals)&#39;       | Queries     | 100000   | InMemory |    19,942.720 μs |         - |        - |   514.38 KB |
| QueryBenchmarks       | &#39;Query: scalar facet (Range)&#39;        | Queries     | 100000   | InMemory |    20,860.026 μs |         - |        - |   514.62 KB |
| QueryBenchmarks       | &#39;Query: array facet (ArrayContains)&#39; | Queries     | 100000   | InMemory |    23,125.820 μs |   31.2500 |        - |  1119.74 KB |
| QueryBenchmarks       | &#39;Reverse reference lookup&#39;           | Queries     | 100000   | InMemory |    50,737.049 μs |   90.9091 |        - |  3123.57 KB |
| **QueryBenchmarks**       | **&#39;Query: metadata (ResourceId)&#39;**       | **Queries**     | **100000**   | **Sqlite**   |    **98,388.079 μs** | **1800.0000** |        **-** | **32835.13 KB** |
| QueryBenchmarks       | &#39;Query: scalar facet (Equals)&#39;       | Queries     | 100000   | Sqlite   |    53,193.085 μs |         - |        - |   769.14 KB |
| QueryBenchmarks       | &#39;Query: scalar facet (Range)&#39;        | Queries     | 100000   | Sqlite   |    65,394.115 μs |         - |        - |   604.59 KB |
| QueryBenchmarks       | &#39;Query: array facet (ArrayContains)&#39; | Queries     | 100000   | Sqlite   |   177,728.743 μs |         - |        - |  4833.45 KB |
| QueryBenchmarks       | &#39;Reverse reference lookup&#39;           | Queries     | 100000   | Sqlite   |   327,655.410 μs |         - |        - | 12875.27 KB |
| **ResourceBenchmarks**    | **Create**                               | **Resources**   | **10000**    | **InMemory** |        **23.533 μs** |    **0.6714** |   **0.2441** |    **11.82 KB** |
| ResourceBenchmarks    | &#39;Update (append a version)&#39;          | Resources   | 10000    | InMemory |        24.265 μs |    0.7324 |   0.1831 |    12.65 KB |
| ResourceBenchmarks    | Activate                             | Resources   | 10000    | InMemory |         6.025 μs |    0.1297 |   0.0610 |     2.27 KB |
| ResourceBenchmarks    | &#39;Get latest&#39;                         | Resources   | 10000    | InMemory |         1.305 μs |    0.0629 |        - |     1.07 KB |
| ResourceBenchmarks    | &#39;Get active&#39;                         | Resources   | 10000    | InMemory |         2.456 μs |    0.0954 |        - |     1.65 KB |
| **ResourceBenchmarks**    | **Create**                               | **Resources**   | **10000**    | **Sqlite**   |       **763.076 μs** |    **1.9531** |        **-** |    **42.12 KB** |
| ResourceBenchmarks    | &#39;Update (append a version)&#39;          | Resources   | 10000    | Sqlite   |       835.075 μs |    3.9063 |        - |    70.16 KB |
| ResourceBenchmarks    | Activate                             | Resources   | 10000    | Sqlite   |       326.200 μs |    0.9766 |        - |    19.17 KB |
| ResourceBenchmarks    | &#39;Get latest&#39;                         | Resources   | 10000    | Sqlite   |        51.271 μs |    0.9155 |        - |    15.84 KB |
| ResourceBenchmarks    | &#39;Get active&#39;                         | Resources   | 10000    | Sqlite   |        60.873 μs |    1.2207 |        - |    21.26 KB |
| **ResourceBenchmarks**    | **Create**                               | **Resources**   | **100000**   | **InMemory** |        **28.422 μs** |    **0.6409** |   **0.2136** |    **11.22 KB** |
| ResourceBenchmarks    | &#39;Update (append a version)&#39;          | Resources   | 100000   | InMemory |        31.779 μs |    0.7324 |   0.1831 |    12.65 KB |
| ResourceBenchmarks    | Activate                             | Resources   | 100000   | InMemory |         9.199 μs |    0.1221 |   0.0458 |     2.27 KB |
| ResourceBenchmarks    | &#39;Get latest&#39;                         | Resources   | 100000   | InMemory |         2.002 μs |    0.0610 |        - |     1.07 KB |
| ResourceBenchmarks    | &#39;Get active&#39;                         | Resources   | 100000   | InMemory |         2.957 μs |    0.0954 |        - |     1.65 KB |
| **ResourceBenchmarks**    | **Create**                               | **Resources**   | **100000**   | **Sqlite**   |       **779.220 μs** |    **1.9531** |        **-** |    **42.12 KB** |
| ResourceBenchmarks    | &#39;Update (append a version)&#39;          | Resources   | 100000   | Sqlite   |       771.661 μs |    3.9063 |        - |    70.18 KB |
| ResourceBenchmarks    | Activate                             | Resources   | 100000   | Sqlite   |       389.733 μs |    0.9766 |        - |    19.17 KB |
| ResourceBenchmarks    | &#39;Get latest&#39;                         | Resources   | 100000   | Sqlite   |        49.840 μs |    0.9155 |        - |    15.84 KB |
| ResourceBenchmarks    | &#39;Get active&#39;                         | Resources   | 100000   | Sqlite   |        62.012 μs |    1.2207 |        - |    21.26 KB |
