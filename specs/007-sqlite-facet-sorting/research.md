# Research: SQLite JSON Facet Sorting

## Decision: Reuse Existing Facet JSON Lookup

SQLite facet filters already resolve facet values through `json_each` over the persisted aspect payload. Facet sorting should reuse that lookup so filtering and sorting resolve named/camel-case facet candidates consistently.

## Decision: Sort Numeric Facets Numerically

Numeric facet values must sort by numeric value, not by text representation. SQLite JSON type information is used to choose numeric ordering for integer and real values.

## Decision: Missing Facets Sort Last

Resources without the sorted facet sort after resources with facet values. This gives predictable list behavior and avoids missing data appearing before meaningful values.

## Decision: Keep Date-Like Range Filtering Unsupported

This feature only adds facet sorting. Date-like range filtering remains outside the SQLite JSON capability declaration.
