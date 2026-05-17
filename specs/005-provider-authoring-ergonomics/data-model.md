# Data Model: Provider Authoring Ergonomics

## Custom Query Provider

Represents a host-provided query executor for the portable `ResourceQuery` model.

### Fields

- `ProviderKey`: Stable non-empty identifier exposed through provider identity.
- `QueryExecution`: Provider-owned behavior for executing supported query shapes.
- `ProviderSpecificGuards`: Execution-time checks for constraints not fully captured by shared validation.

### Validation Rules

- Provider key must be non-empty.
- Provider key must match the active capability declaration for validation to succeed.
- Execution remains authoritative and may reject provider-specific unsupported shapes.

## Custom Capability Declaration

Represents the query shapes a custom provider declares as supported.

### Fields

- `ProviderKey`: Stable key matching the custom query provider.
- `ProviderName`: Human-readable provider name.
- Supported scopes, filters, comparisons, sorting, paging, and value shapes.
- Known unsupported features for discovery and documentation.

### Validation Rules

- Provider key and name must be non-empty.
- If the key does not match the active query provider, validation fails closed.
- Declarations should match execution behavior and be covered by provider tests.

## Provider Registration Recipe

Represents the host-visible registration path for a custom query provider.

### Fields

- `QueryServiceType`: Concrete active query service type.
- `CapabilitiesProviderType`: Concrete capability declaration provider type.
- `InterfaceMappings`: Registrations for execution, provider identity, and capability discovery.

### Validation Rules

- Query service type must implement both query execution and provider identity contracts.
- Capability provider type must implement capability declaration contract.
- Last registration wins for active single-service resolution, matching existing provider replacement behavior.
- Manual registration remains possible.

## Provider Authoring Failure

Represents feedback for provider misconfiguration or unsupported query execution.

### Fields

- `Code`: Stable failure code such as `capabilities-not-declared`.
- `Feature`: Failure category such as `capabilities missing` or provider-specific query feature.
- `Message`: Actionable explanation for provider authors.
- `Path`: Optional query path when the failure maps to a query shape location.

### Validation Rules

- Missing or mismatched active-provider capabilities use `capabilities-not-declared`.
- Messages should include the active provider key when available.
- Execution failures should use structured unsupported-query details.

## State Transitions

### Registration

```text
Host registers defaults
  └─ Host registers custom provider helper
      ├─ query service becomes active service
      ├─ provider identity resolves to custom provider
      └─ capability provider declaration is discoverable
```

### Validation

```text
Active provider key
  ├─ matching capability declaration exists → validate query with custom capabilities
  └─ no matching declaration exists → capabilities-not-declared with actionable message
```
