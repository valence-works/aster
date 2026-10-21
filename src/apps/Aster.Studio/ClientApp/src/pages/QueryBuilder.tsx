import { useEffect, useState } from "react";
import { CheckCircle2, ListFilter, Play, Plus, ServerCog, Trash2, XCircle } from "lucide-react";
import { Link } from "react-router-dom";
import { toast } from "sonner";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { ErrorState, TableSkeleton } from "@/components/StateViews";
import { api, ApiError, type ComparisonOperator, type LifecycleFilter, type QueryCapabilities, type QueryCondition, type QueryRequest, type QueryResultDto, type QueryValidationDto, type QueryValidationFailure, type SharedAspectDto, type VersionScope } from "@/lib/api";
import { formatDate } from "@/lib/utils";
import { useAsync } from "@/hooks/useAsync";

type ConditionRow = Required<Pick<QueryCondition, "kind" | "operator">> & { key: number; field?: string; aspectKey?: string; facetId?: string; raw: string; min: string; max: string };
type SortRow = { key: number; target?: string; descending: boolean };

const operators: ComparisonOperator[] = ["equals", "not-equals", "in", "contains", "starts-with", "range", "exists", "array-contains", "array-contains-any", "array-contains-all"];
const listOperators: ComparisonOperator[] = ["in", "array-contains-any", "array-contains-all"];
const kinds: Array<{ value: QueryCondition["kind"]; label: string }> = [
  { value: "facet", label: "Facet value" },
  { value: "metadata", label: "Metadata field" },
  { value: "exists", label: "Aspect present" },
  { value: "references", label: "References resource" },
];
const scopes: Array<{ value: VersionScope; label: string }> = [
  { value: "latest", label: "Latest versions" },
  { value: "all-versions", label: "All versions" },
  { value: "active", label: "Active in channel" },
  { value: "draft", label: "Drafts (never activated)" },
];
const lifecycles: Array<{ value: LifecycleFilter; label: string }> = [
  { value: "active", label: "Active" },
  { value: "archived", label: "Archived" },
  { value: "soft-deleted", label: "Soft-deleted" },
  { value: "all", label: "All lifecycle states" },
];

let nextKey = 0;
const newCondition = (): ConditionRow => ({ key: nextKey++, kind: "facet", operator: "equals", raw: "", min: "", max: "" });

// Sort targets are encoded as "metadata:Field" or "facet:aspectKey:facetId".
function toSort({ target = "", descending }: SortRow) {
  const [kind, first, second] = target.split(":");
  return kind === "facet" ? { field: second, aspectKey: first, descending } : { field: first, aspectKey: null, descending };
}

function isComplete(row: ConditionRow) {
  if (row.kind === "metadata") return !!row.field;
  if (row.kind === "exists") return !!row.aspectKey;
  return !!row.aspectKey && !!row.facetId && (row.kind !== "references" || !!row.raw.trim());
}

/** The SDK's paths for condition i start with Filter.Operands[i], because conditions are the filter's operands. */
function failuresAt(failures: QueryValidationFailure[], prefix: string) {
  return failures.filter(({ path }) => path === prefix || path?.startsWith(`${prefix}.`) || path?.startsWith(`${prefix}[`));
}

/** Composes a ResourceQuery, shows IResourceQueryValidator's failures as it changes, and runs it once valid (#112). */
export function QueryBuilder() {
  const sources = useAsync(async () => {
    const [capabilities, definitions, aspects] = await Promise.all([api.getQueryCapabilities(), api.getDefinitions(), api.getAspects()]);
    return { capabilities, definitions, aspects };
  }, []);
  const [definitionId, setDefinitionId] = useState("any");
  const [scope, setScope] = useState<VersionScope>("latest");
  const [channel, setChannel] = useState("live");
  const [lifecycle, setLifecycle] = useState<LifecycleFilter>("active");
  const [match, setMatch] = useState<"and" | "or">("and");
  const [conditions, setConditions] = useState<ConditionRow[]>([]);
  const [sorts, setSorts] = useState<SortRow[]>([]);
  const [take, setTake] = useState(20);
  const [validation, setValidation] = useState<{ key: string; result: QueryValidationDto | null; error?: string } | null>(null);
  const [results, setResults] = useState<QueryResultDto | null>(null);
  const [running, setRunning] = useState(false);

  const aspects = sources.data?.aspects ?? [];
  const facetType = (aspectKey?: string, facetId?: string) => aspects.find((aspect) => aspect.aspectId === aspectKey)?.facets.find((facet) => facet.facetId === facetId)?.type;

  function toCondition(row: ConditionRow): QueryCondition {
    const { kind, field, aspectKey, facetId, operator } = row;
    const numeric = kind === "metadata" ? field === "Version" : facetType(aspectKey, facetId)?.startsWith("number") === true;
    const typed = (text: string) => (numeric && text.trim() !== "" && !Number.isNaN(Number(text)) ? Number(text) : text.trim());
    const value = kind === "exists" ? undefined
      : kind === "references" ? row.raw.trim()
      : operator === "exists" ? true
      : operator === "range" ? { min: row.min.trim() ? typed(row.min) : null, max: row.max.trim() ? typed(row.max) : null }
      : listOperators.includes(operator) ? row.raw.split(",").map((part) => part.trim()).filter(Boolean).map(typed)
      : typed(row.raw);
    return { kind, field, aspectKey, facetId, operator, value };
  }

  // The 1-based number of the first incomplete condition, or 0. Sorts without a target are left out instead.
  const incomplete = conditions.findIndex((row) => !isComplete(row)) + 1;
  const query: QueryRequest = {
    definitionId: definitionId === "any" ? null : definitionId,
    scope,
    channel: scope === "active" ? channel : null,
    lifecycle,
    match,
    conditions: conditions.map(toCondition),
    sort: sorts.filter((row) => row.target).map(toSort),
    take,
  };
  const queryKey = JSON.stringify(query);
  const current = validation?.key === queryKey ? validation.result : null;
  const validationError = validation?.key === queryKey ? validation.error : undefined;

  useEffect(() => {
    if (!sources.data || incomplete) return;
    let cancelled = false;
    const timer = setTimeout(() => {
      api.validateQuery(query)
        .then((result) => { if (!cancelled) setValidation({ key: queryKey, result }); })
        .catch((error: unknown) => {
          if (cancelled) return;
          const message = error instanceof Error ? error.message : String(error);
          setValidation({ key: queryKey, result: null, error: message });
          toast.error("Validation failed", { description: message });
        });
    }, 250);
    return () => {
      cancelled = true;
      clearTimeout(timer);
    };
  }, [queryKey, incomplete, sources.data]);

  async function run() {
    setRunning(true);
    try {
      setResults(await api.runQuery(query));
    } catch (error) {
      const codes = error instanceof ApiError ? error.diagnostics?.map((diagnostic) => diagnostic.code).join(", ") : undefined;
      toast.error("Query failed", { description: codes || (error instanceof Error ? error.message : String(error)) });
    } finally {
      setRunning(false);
    }
  }

  const update = (key: number, change: Partial<ConditionRow>) => setConditions((rows) => rows.map((row) => (row.key === key ? { ...row, ...change } : row)));
  const updateSort = (key: number, change: Partial<SortRow>) => setSorts((rows) => rows.map((row) => (row.key === key ? { ...row, ...change } : row)));

  if (sources.loading) return <TableSkeleton />;
  if (sources.error || !sources.data) return <ErrorState error={sources.error} onRetry={sources.reload} />;

  const { capabilities, definitions } = sources.data;
  const failures = current?.failures ?? [];
  const sortTargets = [
    ...capabilities.supportedMetadataFields.map((field) => ({ value: `metadata:${field}`, label: field })),
    ...aspects.flatMap((aspect) => aspect.facets.map((facet) => ({ value: `facet:${aspect.aspectId}:${facet.facetId}`, label: `${aspect.aspectId}.${facet.facetId}` }))),
  ];

  return (
    <div className="space-y-6">
      <div><h2 className="text-2xl font-semibold tracking-[-0.05em]">Query Builder</h2><p className="mt-1 text-sm text-muted-foreground">Compose a portable ResourceQuery. Each change is checked by IResourceQueryValidator against the provider's declared capabilities; only a valid query runs.</p></div>
      <div className="grid gap-6 xl:grid-cols-[1fr_360px]">
        <div className="space-y-6">
          <Card>
            <CardHeader className="border-b border-border/60"><CardTitle className="flex items-center gap-2"><ListFilter className="h-5 w-5 text-primary" />Query</CardTitle><CardDescription>Content type, version scope and lifecycle, then conditions and sorting.</CardDescription></CardHeader>
            <CardContent className="space-y-5 pt-4">
              <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
                <div className="space-y-2"><Label>Content type</Label><Select value={definitionId} onValueChange={setDefinitionId}><SelectTrigger aria-label="Content type"><SelectValue /></SelectTrigger><SelectContent><SelectItem value="any">Any content type</SelectItem>{definitions.map((definition) => <SelectItem key={definition.definitionId} value={definition.definitionId}>{definition.displayName}</SelectItem>)}</SelectContent></Select></div>
                <div className="space-y-2"><Label>Versions</Label><Select value={scope} onValueChange={(value) => setScope(value as VersionScope)}><SelectTrigger aria-label="Version scope"><SelectValue /></SelectTrigger><SelectContent>{scopes.map((option) => <SelectItem key={option.value} value={option.value}>{option.label}</SelectItem>)}</SelectContent></Select></div>
                {scope === "active" ? <div className="space-y-2"><Label htmlFor="query-channel">Channel</Label><Input id="query-channel" value={channel} onChange={(event) => setChannel(event.target.value)} /></div> : null}
                <div className="space-y-2"><Label>Lifecycle</Label><Select value={lifecycle} onValueChange={(value) => setLifecycle(value as LifecycleFilter)}><SelectTrigger aria-label="Lifecycle"><SelectValue /></SelectTrigger><SelectContent>{lifecycles.map((option) => <SelectItem key={option.value} value={option.value}>{option.label}</SelectItem>)}</SelectContent></Select></div>
              </div>
              <div className="space-y-3">
                <div className="flex flex-wrap items-center gap-3">
                  <span className="text-sm font-medium">Conditions</span>
                  <Select value={match} onValueChange={(value) => setMatch(value as "and" | "or")}><SelectTrigger aria-label="Match" className="w-44"><SelectValue /></SelectTrigger><SelectContent><SelectItem value="and">Match all (and)</SelectItem><SelectItem value="or">Match any (or)</SelectItem></SelectContent></Select>
                  <Button variant="outline" size="sm" onClick={() => setConditions((rows) => [...rows, newCondition()])}><Plus className="h-4 w-4" />Add condition</Button>
                </div>
                {conditions.length === 0 ? <p className="text-sm text-muted-foreground">No conditions: the query returns every resource in scope.</p> : null}
                {conditions.map((row, index) => (
                  <ConditionEditor key={row.key} index={index} row={row} aspects={aspects} capabilities={capabilities} failures={failuresAt(failures, `Filter.Operands[${index}]`)} onChange={(change) => update(row.key, change)} onRemove={() => setConditions((rows) => rows.filter((candidate) => candidate.key !== row.key))} />
                ))}
              </div>
              <div className="space-y-3">
                <div className="flex flex-wrap items-center gap-3"><span className="text-sm font-medium">Sort</span><Button variant="outline" size="sm" onClick={() => setSorts((rows) => [...rows, { key: nextKey++, descending: false }])}><Plus className="h-4 w-4" />Add sort</Button></div>
                {sorts.map((row, index) => (
                  <div key={row.key} role="group" aria-label={`Sort ${index + 1}`} className="flex flex-wrap items-center gap-2">
                    <Select value={row.target} onValueChange={(target) => updateSort(row.key, { target })}><SelectTrigger aria-label="Sort by" className="w-64"><SelectValue placeholder="Sort by" /></SelectTrigger><SelectContent>{sortTargets.map((target) => <SelectItem key={target.value} value={target.value}>{target.label}</SelectItem>)}</SelectContent></Select>
                    <Select value={row.descending ? "desc" : "asc"} onValueChange={(value) => updateSort(row.key, { descending: value === "desc" })}><SelectTrigger aria-label="Direction" className="w-36"><SelectValue /></SelectTrigger><SelectContent><SelectItem value="asc">Ascending</SelectItem><SelectItem value="desc">Descending</SelectItem></SelectContent></Select>
                    <Button variant="ghost" size="icon" aria-label="Remove sort" onClick={() => setSorts((rows) => rows.filter((candidate) => candidate.key !== row.key))}><Trash2 className="h-4 w-4" /></Button>
                    <FailureList failures={failuresAt(failures, `Sorts[${index}]`)} />
                  </div>
                ))}
              </div>
              <div className="flex flex-wrap items-end gap-3">
                <div className="space-y-2"><Label htmlFor="query-take">Take</Label><Input id="query-take" type="number" className="w-28" value={take} onChange={(event) => setTake(Number(event.target.value))} /></div>
                <Button onClick={run} disabled={running || !current?.summary.isValid}><Play className="h-4 w-4" />Run query</Button>
              </div>
            </CardContent>
          </Card>
          {results ? (
            <Card>
              <CardHeader><CardTitle>Results</CardTitle><CardDescription>{results.items.length} resources{results.hasMore ? `; more match beyond the first ${take}` : ""}.</CardDescription></CardHeader>
              <CardContent>
                {results.items.length === 0 ? <p className="text-sm text-muted-foreground">No resources match.</p> : (
                  <Table aria-label="Query results">
                    <TableHeader><TableRow><TableHead>Title</TableHead><TableHead>Type</TableHead><TableHead>Version</TableHead><TableHead>Created</TableHead></TableRow></TableHeader>
                    <TableBody>{results.items.map((item) => <TableRow key={`${item.resourceId}-${item.version}`}><TableCell><Link className="font-medium hover:text-primary" to={`/content/${item.definitionId}/${encodeURIComponent(item.resourceId)}`}>{item.title}</Link><div className="text-xs text-muted-foreground">{item.resourceId}</div></TableCell><TableCell><Badge variant="schema">{item.definitionId}</Badge></TableCell><TableCell className="font-mono">v{item.version}</TableCell><TableCell>{formatDate(item.created)}</TableCell></TableRow>)}</TableBody>
                  </Table>
                )}
              </CardContent>
            </Card>
          ) : null}
        </div>
        <div className="space-y-6">
          <Card>
            <CardHeader><CardTitle>Validation</CardTitle><CardDescription>Failures carry a stable code and the path of the query part they concern.</CardDescription></CardHeader>
            <CardContent>
              <section aria-label="Validation" className="space-y-3 text-sm">
                {incomplete ? <p className="text-muted-foreground">Complete condition {incomplete} to validate.</p>
                  : validationError ? <p className="flex items-center gap-2 text-destructive"><XCircle className="h-4 w-4" />Could not validate: {validationError}</p>
                  : !current ? <p className="text-muted-foreground">Validating…</p>
                  : current.summary.isValid ? <p className="flex items-center gap-2 text-emerald-300"><CheckCircle2 className="h-4 w-4" />Valid for {capabilities.providerName}</p>
                  : <><p className="flex items-center gap-2 text-destructive"><XCircle className="h-4 w-4" />{current.summary.totalFailureCount} validation failures</p><FailureList failures={current.failures} /></>}
              </section>
            </CardContent>
          </Card>
          <CapabilitiesCard capabilities={capabilities} />
        </div>
      </div>
    </div>
  );
}

function ConditionEditor({ index, row, aspects, capabilities, failures, onChange, onRemove }: { index: number; row: ConditionRow; aspects: SharedAspectDto[]; capabilities: QueryCapabilities; failures: QueryValidationFailure[]; onChange: (change: Partial<ConditionRow>) => void; onRemove: () => void }) {
  const facets = aspects.find((aspect) => aspect.aspectId === row.aspectKey)?.facets ?? [];
  const supported = capabilities.supportedComparisonOperators[row.kind === "metadata" ? "metadata" : "facet-value"] ?? [];
  const hasOperator = row.kind === "metadata" || row.kind === "facet";

  return (
    <div role="group" aria-label={`Condition ${index + 1}`} className={`space-y-2 rounded-lg border p-3 ${failures.length ? "border-destructive/50 bg-destructive/5" : "border-border/70 bg-background/35"}`}>
      <div className="flex flex-wrap items-center gap-2">
        <Select value={row.kind} onValueChange={(kind) => onChange({ kind: kind as ConditionRow["kind"] })}><SelectTrigger aria-label="Kind" className="w-44"><SelectValue /></SelectTrigger><SelectContent>{kinds.map((kind) => <SelectItem key={kind.value} value={kind.value}>{kind.label}</SelectItem>)}</SelectContent></Select>
        {row.kind === "metadata" ? (
          <Select value={row.field} onValueChange={(field) => onChange({ field })}><SelectTrigger aria-label="Field" className="w-40"><SelectValue placeholder="Field" /></SelectTrigger><SelectContent>{capabilities.supportedMetadataFields.map((field) => <SelectItem key={field} value={field}>{field}</SelectItem>)}</SelectContent></Select>
        ) : (
          <Select value={row.aspectKey} onValueChange={(aspectKey) => onChange({ aspectKey, facetId: undefined })}><SelectTrigger aria-label="Aspect" className="w-40"><SelectValue placeholder="Aspect" /></SelectTrigger><SelectContent>{aspects.map((aspect) => <SelectItem key={aspect.aspectId} value={aspect.aspectId}>{aspect.aspectId}</SelectItem>)}</SelectContent></Select>
        )}
        {row.kind === "facet" || row.kind === "references" ? (
          <Select value={row.facetId} onValueChange={(facetId) => onChange({ facetId })}><SelectTrigger aria-label="Facet" className="w-44"><SelectValue placeholder="Facet" /></SelectTrigger><SelectContent>{facets.map((facet) => <SelectItem key={facet.facetId} value={facet.facetId}>{facet.facetId} · {facet.type}</SelectItem>)}</SelectContent></Select>
        ) : null}
        {hasOperator ? (
          <Select value={row.operator} onValueChange={(operator) => onChange({ operator: operator as ComparisonOperator })}><SelectTrigger aria-label="Operator" className="w-48"><SelectValue /></SelectTrigger><SelectContent>{operators.map((operator) => <SelectItem key={operator} value={operator}>{operator}{supported.includes(operator) ? "" : " (unsupported)"}</SelectItem>)}</SelectContent></Select>
        ) : null}
        {row.kind === "references" ? <Input aria-label="Target resource ID" className="w-56" placeholder="Referenced resource ID" value={row.raw} onChange={(event) => onChange({ raw: event.target.value })} />
          : !hasOperator || row.operator === "exists" ? null
          : row.operator === "range" ? <><Input aria-label="Minimum" className="w-32" placeholder="min" value={row.min} onChange={(event) => onChange({ min: event.target.value })} /><Input aria-label="Maximum" className="w-32" placeholder="max" value={row.max} onChange={(event) => onChange({ max: event.target.value })} /></>
          : <Input aria-label={listOperators.includes(row.operator) ? "Values" : "Value"} className="w-56" placeholder={listOperators.includes(row.operator) ? "comma-separated" : "value"} value={row.raw} onChange={(event) => onChange({ raw: event.target.value })} />}
        <Button variant="ghost" size="icon" aria-label="Remove condition" className="ml-auto" onClick={onRemove}><Trash2 className="h-4 w-4" /></Button>
      </div>
      <FailureList failures={failures} />
    </div>
  );
}

function FailureList({ failures }: { failures: QueryValidationFailure[] }) {
  if (!failures.length) return null;
  return (
    <ul aria-label="Validation failures" className="w-full space-y-1.5">
      {failures.map((failure, index) => <li key={`${failure.code}-${index}`} className="flex flex-wrap items-baseline gap-2 text-sm"><code className="rounded bg-destructive/15 px-1.5 py-0.5 font-mono text-xs text-destructive">{failure.code}</code>{failure.path ? <code className="font-mono text-xs text-muted-foreground">{failure.path}</code> : null}<span className="text-muted-foreground">{failure.message}</span></li>)}
    </ul>
  );
}

function CapabilitiesCard({ capabilities }: { capabilities: QueryCapabilities }) {
  const rows: Array<[string, string]> = [
    ["Version scopes", capabilities.supportedScopes.join(", ") + (capabilities.requiresActivationChannelForActiveScope ? " (active needs a channel)" : "")],
    ["Filters", capabilities.supportedFilterTypes.join(", ")],
    ["Logical operators", capabilities.supportedLogicalOperators.join(", ")],
    ["Metadata operators", capabilities.supportedComparisonOperators.metadata?.join(", ") ?? "none"],
    ["Facet operators", capabilities.supportedComparisonOperators["facet-value"]?.join(", ") ?? "none"],
    ["Metadata fields", capabilities.supportedMetadataFields.join(", ")],
    ["Text search on", capabilities.metadataContainsFields.join(", ")],
    ["Sorting", [capabilities.supportsMetadataSorting ? "metadata" : null, capabilities.supportsFacetSorting ? "facets" : null].filter(Boolean).join(", ") || "none"],
    ["Facet ranges", capabilities.facetRangeSupport.join(", ")],
    ["Array values", capabilities.maxArrayQueryElements ? `up to ${capabilities.maxArrayQueryElements}` : "unbounded"],
    ["Lifecycle filter", capabilities.supportsLifecycleStateFiltering ? "supported" : "not supported"],
    ["Unsupported", capabilities.unsupportedFeatures.join(", ") || "none declared"],
  ];
  return (
    <Card>
      <CardHeader><CardTitle className="flex items-center gap-2"><ServerCog className="h-5 w-5 text-primary" />Provider capabilities</CardTitle><CardDescription>{capabilities.providerName} <span className="font-mono">({capabilities.providerKey})</span>, from IResourceQueryCapabilitiesProvider.</CardDescription></CardHeader>
      <CardContent>
        <dl aria-label="Provider capabilities" className="space-y-2 text-sm">{rows.map(([term, detail]) => <div key={term}><dt className="text-xs uppercase tracking-wide text-muted-foreground">{term}</dt><dd>{detail}</dd></div>)}</dl>
      </CardContent>
    </Card>
  );
}
