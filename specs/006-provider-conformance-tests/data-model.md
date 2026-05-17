# Data Model: Provider Conformance Tests

## Provider Conformance Subject

- `Name`: Human-readable provider label used in failures.
- `ExpectedProviderKey`: Provider key expected from active identity and capability declarations.
- `Services`: Explicit service provider containing active query provider, validator, and capability declarations.
- `RequiresNonEmptyResults`: Whether supported positive cases must match fixture data.

## Conformance Query Case

- `Name`: Focused case name.
- `Area`: Capability area, such as scope, filter, sort, or paging.
- `Query`: Portable resource query shape to validate and execute.
- `RequiresMatch`: Whether the query should return at least one fixture result for providers seeded with data.

## Conformance Failure

- `ProviderName`: Subject that failed.
- `CaseName`: Query case or registration check that failed.
- `Area`: Capability area under test.
- `Message`: Focused explanation of the mismatch.
