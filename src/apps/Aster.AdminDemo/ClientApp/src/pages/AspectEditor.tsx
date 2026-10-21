import { useEffect, useMemo, useState } from "react";
import { ArrowDown, ArrowUp, ArrowUpCircle, CheckCircle2, Plus, Save, Trash2 } from "lucide-react";
import { Link, useNavigate, useOutletContext, useParams } from "react-router-dom";
import { toast } from "sonner";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Checkbox } from "@/components/ui/checkbox";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Switch } from "@/components/ui/switch";
import { ConstraintBadges } from "@/components/FacetBadges";
import { ErrorState, TableSkeleton } from "@/components/StateViews";
import { ApiError, api, type AspectUpgradeCandidateDto, type DiagnosticDto, type FacetDto, type FacetMetadata, type SharedAspectDto } from "@/lib/api";
import { useAsync } from "@/hooks/useAsync";

type ShellContext = { reloadDefinitions: () => void };
type FacetRow = FacetDto & { key: string };

const facetTypes = ["string", "number", "boolean", "datetime", "guid", "reference", "string[]", "number[]", "reference[]"];
const uiHints = ["text", "multiline", "richtext", "slug", "email", "url", "date", "number", "toggle", "select", "tags"];
const noHint = "none";

let nextRowKey = 0;
const toRows = (facets: FacetDto[]): FacetRow[] => facets.map((facet) => ({ ...facet, key: `row-${nextRowKey++}` }));

// Row order is display order; empty text metadata is dropped so it doesn't count as a change.
function toFacets(rows: FacetRow[]): FacetDto[] {
  return rows.map((row, index) => {
    const metadata: FacetMetadata = { ...row.metadata, displayOrder: index + 1 };
    for (const field of ["label", "description", "uiHint"] as const) {
      if (!metadata[field]?.trim()) delete metadata[field];
    }
    return { facetId: row.facetId.trim(), type: row.type, isRequired: row.isRequired, metadata };
  });
}

export function AspectEditor() {
  const { aspectId } = useParams();
  return aspectId ? <EditAspect aspectId={aspectId} /> : <NewAspect />;
}

function NewAspect() {
  const navigate = useNavigate();
  return (
    <AspectForm
      title="New aspect"
      description="Name the aspect and declare its facets. It is stored as version 1 and can be attached to any content type."
      initialFacets={[{ facetId: "", type: "string", isRequired: false, metadata: null }]}
      initialRequiresName={false}
      onSave={async (aspectId, requiresName, facets) => {
        const created = await api.createAspect({ aspectId, requiresName, facets });
        toast.success(`${created.displayName} created`, { description: "Stored as version 1." });
        navigate(`/aspects/${encodeURIComponent(created.aspectId)}`);
      }}
    />
  );
}

function EditAspect({ aspectId }: { aspectId: string }) {
  const aspect = useAsync(() => api.getAspect(aspectId), [aspectId]);
  const { reloadDefinitions } = useOutletContext<ShellContext>();
  if (aspect.loading && !aspect.data) return <TableSkeleton />;
  if (aspect.error || !aspect.data) return <ErrorState error={aspect.error} onRetry={aspect.reload} />;

  const current = aspect.data;
  return (
    <div className="space-y-6">
      <AspectForm
        key={`${current.aspectId}-${current.version}`}
        aspect={current}
        title={current.displayName}
        description={`Version ${current.version} of ${current.versions.length}. Saving adds a new version; content types keep their pinned version until you upgrade them below.`}
        initialFacets={current.facets}
        initialRequiresName={current.requiresName}
        onSave={async (_, requiresName, facets) => {
          const saved = await api.updateAspect(current.aspectId, { baseVersion: current.version, requiresName, facets });
          if (saved.version === current.version) toast.info("No changes to save");
          else toast.success(`${saved.displayName} saved as version ${saved.version}`);
          aspect.setData(saved);
        }}
      />
      <UpgradePanel aspect={current} onUpgraded={() => { aspect.reload(); reloadDefinitions(); }} />
    </div>
  );
}

function AspectForm({ aspect, title, description, initialFacets, initialRequiresName, onSave }: {
  aspect?: SharedAspectDto;
  title: string;
  description: string;
  initialFacets: FacetDto[];
  initialRequiresName: boolean;
  onSave: (aspectId: string, requiresName: boolean, facets: FacetDto[]) => Promise<void>;
}) {
  const [aspectId, setAspectId] = useState(aspect?.aspectId ?? "");
  const [requiresName, setRequiresName] = useState(initialRequiresName);
  const [rows, setRows] = useState(() => toRows(initialFacets));
  const [diagnostics, setDiagnostics] = useState<DiagnosticDto[]>([]);
  const [saving, setSaving] = useState(false);

  const initial = useMemo(() => JSON.stringify([initialRequiresName, toFacets(toRows(initialFacets))]), [initialFacets, initialRequiresName]);
  const dirty = JSON.stringify([requiresName, toFacets(rows)]) !== initial;

  const update = (key: string, change: (row: FacetRow) => FacetRow) => setRows((current) => current.map((row) => (row.key === key ? change(row) : row)));
  const updateMetadata = (key: string, field: "label" | "description" | "uiHint", value: string) =>
    update(key, (row) => ({ ...row, metadata: { ...row.metadata, [field]: value } }));
  const move = (index: number, offset: number) => setRows((current) => {
    const next = [...current];
    [next[index], next[index + offset]] = [next[index + offset], next[index]];
    return next;
  });

  async function save() {
    setSaving(true);
    setDiagnostics([]);
    try {
      await onSave(aspectId.trim(), requiresName, toFacets(rows));
    } catch (error) {
      if (error instanceof ApiError && error.diagnostics) setDiagnostics(error.diagnostics);
      toast.error("Could not save aspect", { description: error instanceof Error ? error.message : String(error) });
    } finally {
      setSaving(false);
    }
  }

  return (
    <Card className="facet-seam">
      <CardHeader>
        <div className="flex flex-wrap items-start justify-between gap-3">
          <div>
            <CardTitle className="text-2xl">{title}</CardTitle>
            <CardDescription>{description}</CardDescription>
          </div>
          {aspect ? <div className="flex flex-wrap gap-1.5"><Badge variant="facet">{aspect.aspectId}</Badge><Badge variant="schema">v{aspect.version}</Badge></div> : null}
        </div>
      </CardHeader>
      <CardContent className="space-y-5">
        <div className="grid gap-4 md:grid-cols-[280px_1fr]">
          {aspect ? null : (
            <div className="space-y-2">
              <Label htmlFor="aspectId">Aspect id</Label>
              <Input id="aspectId" value={aspectId} onChange={(event) => setAspectId(event.target.value)} placeholder="pricing" />
              <p className="text-xs text-muted-foreground">Letters, digits, '-' or '_'; it becomes the aspect key on resources.</p>
            </div>
          )}
          <div className="flex items-center justify-between gap-4 rounded-md border border-border/70 bg-secondary/35 p-3 md:col-start-2">
            <div><Label>Requires a name</Label><p className="text-xs text-muted-foreground">Content types must attach it under a name, so one type can hold several (e.g. listing and sale price).</p></div>
            <Switch checked={requiresName} onCheckedChange={setRequiresName} />
          </div>
        </div>
        <div className="space-y-3">
          {rows.map((row, index) => (
            <div key={row.key} className="rounded-md border border-border/60 bg-background/35 p-4">
              <div className="grid gap-3 md:grid-cols-[1fr_160px_1fr_150px_auto]">
                <div className="space-y-1"><Label className="text-xs">Facet id</Label><Input value={row.facetId} onChange={(event) => update(row.key, (current) => ({ ...current, facetId: event.target.value }))} placeholder="amount" /></div>
                <div className="space-y-1">
                  <Label className="text-xs">Type</Label>
                  <Select value={row.type} onValueChange={(type) => update(row.key, (current) => ({ ...current, type }))}>
                    <SelectTrigger><SelectValue /></SelectTrigger>
                    <SelectContent>{(facetTypes.includes(row.type) ? facetTypes : [row.type, ...facetTypes]).map((type) => <SelectItem key={type} value={type}>{type}</SelectItem>)}</SelectContent>
                  </Select>
                </div>
                <div className="space-y-1"><Label className="text-xs">Label</Label><Input value={row.metadata?.label ?? ""} onChange={(event) => updateMetadata(row.key, "label", event.target.value)} placeholder={row.facetId || "Label"} /></div>
                <div className="space-y-1">
                  <Label className="text-xs">UI hint</Label>
                  <Select value={row.metadata?.uiHint || noHint} onValueChange={(hint) => updateMetadata(row.key, "uiHint", hint === noHint ? "" : hint)}>
                    <SelectTrigger><SelectValue /></SelectTrigger>
                    <SelectContent>{[noHint, ...uiHints, ...(row.metadata?.uiHint && !uiHints.includes(row.metadata.uiHint) ? [row.metadata.uiHint] : [])].map((hint) => <SelectItem key={hint} value={hint}>{hint}</SelectItem>)}</SelectContent>
                  </Select>
                </div>
                <div className="flex items-end gap-1">
                  <Button type="button" variant="ghost" size="icon" aria-label={`Move ${row.facetId || "facet"} up`} disabled={index === 0} onClick={() => move(index, -1)}><ArrowUp className="h-4 w-4" /></Button>
                  <Button type="button" variant="ghost" size="icon" aria-label={`Move ${row.facetId || "facet"} down`} disabled={index === rows.length - 1} onClick={() => move(index, 1)}><ArrowDown className="h-4 w-4" /></Button>
                  <Button type="button" variant="ghost" size="icon" aria-label={`Remove ${row.facetId || "facet"}`} disabled={rows.length === 1} onClick={() => setRows((current) => current.filter((item) => item.key !== row.key))}><Trash2 className="h-4 w-4" /></Button>
                </div>
              </div>
              <div className="mt-3 grid gap-3 md:grid-cols-[1fr_auto] md:items-end">
                <div className="space-y-1"><Label className="text-xs">Description</Label><Input value={row.metadata?.description ?? ""} onChange={(event) => updateMetadata(row.key, "description", event.target.value)} placeholder="Shown as help text in the generated form" /></div>
                <div className="flex items-center gap-2 pb-2"><Switch checked={row.isRequired} onCheckedChange={(isRequired) => update(row.key, (current) => ({ ...current, isRequired }))} /><span className="text-sm">Required</span></div>
              </div>
              <div className="mt-3 text-xs"><ConstraintBadges facet={row} /></div>
            </div>
          ))}
        </div>
        {diagnostics.length ? <div className="rounded-md border border-destructive/30 bg-destructive/10 p-3 text-sm text-destructive">{diagnostics.map((diagnostic, index) => <div key={index}>{diagnostic.code}{diagnostic.facetId ? ` (${diagnostic.facetId})` : ""}: {diagnostic.message}</div>)}</div> : null}
        <div className="flex flex-wrap justify-between gap-3">
          <Button type="button" variant="outline" onClick={() => setRows((current) => [...current, ...toRows([{ facetId: "", type: "string", isRequired: false, metadata: null }])])}><Plus className="h-4 w-4" />Add facet</Button>
          <Button type="button" onClick={save} disabled={saving || !dirty || (!aspect && !aspectId.trim())}><Save className="h-4 w-4" />{saving ? "Saving…" : aspect ? "Save as new version" : "Create aspect"}</Button>
        </div>
      </CardContent>
    </Card>
  );
}

function UpgradePanel({ aspect, onUpgraded }: { aspect: SharedAspectDto; onUpgraded: () => void }) {
  const preview = useAsync(() => api.previewAspectUpgrade(aspect.aspectId), [aspect.aspectId, aspect.version]);
  const [selected, setSelected] = useState<string[]>([]);
  const [applying, setApplying] = useState(false);

  useEffect(() => setSelected([]), [preview.data]);

  const byDefinition = useMemo(() => {
    const groups = new Map<string, AspectUpgradeCandidateDto[]>();
    for (const candidate of preview.data?.candidates ?? []) groups.set(candidate.definitionId, [...(groups.get(candidate.definitionId) ?? []), candidate]);
    return [...groups.entries()];
  }, [preview.data]);
  const current = aspect.usedBy.filter((usage) => usage.pinnedVersion === aspect.version);

  async function apply() {
    if (preview.data?.targetVersion == null) return;
    setApplying(true);
    try {
      const result = await api.applyAspectUpgrade(aspect.aspectId, preview.data.targetVersion, selected);
      for (const outcome of result.outcomes) {
        if (outcome.status === "upgraded") toast.success(`${outcome.definitionId} now pins v${result.targetVersion}`, { description: `Content type version ${outcome.newDefinitionVersion}; its editor form uses the new facets.` });
        else if (outcome.status === "failed") toast.error(`${outcome.definitionId} was not upgraded`, { description: outcome.diagnostics.map((diagnostic) => diagnostic.message).join(" ") });
      }
      onUpgraded();
    } catch (error) {
      toast.error("Upgrade failed", { description: error instanceof Error ? error.message : String(error) });
    } finally {
      setApplying(false);
    }
  }

  return (
    <Card>
      <CardHeader className="border-b border-border/60">
        <CardTitle className="flex items-center gap-2"><ArrowUpCircle className="h-5 w-5 text-amber-300" />Content type upgrades</CardTitle>
        <CardDescription>Content types pin an aspect version. Upgrading one registers a new content type version pinned to v{aspect.version}; existing resources keep their data.</CardDescription>
      </CardHeader>
      <CardContent className="space-y-4 pt-6">
        {preview.loading && !preview.data ? <p className="text-sm text-muted-foreground">Checking content types…</p> : null}
        {preview.error ? <ErrorState error={preview.error} onRetry={preview.reload} /> : null}
        {preview.data && byDefinition.length === 0 ? <p className="flex items-center gap-2 text-sm text-muted-foreground"><CheckCircle2 className="h-4 w-4 text-emerald-300" />Every content type using this aspect is on the latest version.</p> : null}
        {byDefinition.map(([definitionId, candidates]) => {
          const checked = selected.includes(definitionId);
          const toggle = () => setSelected((items) => (checked ? items.filter((item) => item !== definitionId) : [...items, definitionId]));
          return (
            <div key={definitionId} className={`rounded-lg border p-4 ${checked ? "border-primary/60 bg-primary/10" : "border-border/70 bg-card/70"}`}>
              <label className="flex cursor-pointer items-center gap-3">
                <Checkbox checked={checked} onCheckedChange={toggle} aria-label={`Upgrade ${definitionId}`} />
                <Link to={`/content-types/${encodeURIComponent(definitionId)}`} className="font-medium hover:text-primary">{definitionId}</Link>
                <span className="text-xs text-muted-foreground">content type v{candidates[0].definitionVersion}</span>
              </label>
              {candidates.map((candidate) => (
                <div key={candidate.attachmentKey} className="mt-3 space-y-2 pl-7">
                  <div className="flex flex-wrap items-center gap-2 text-sm"><Badge variant="secondary">{candidate.attachmentKey}</Badge><span className="font-mono text-xs">v{candidate.pinnedVersion} → v{preview.data?.targetVersion}</span></div>
                  {candidate.changes.map((change) => (
                    <div key={`${change.facetId}-${change.kind}`} className="flex flex-wrap items-center gap-2 text-xs">
                      <Badge variant={change.kind === "removed" ? "destructive" : change.kind === "added" ? "live" : "preview"}>{change.kind}</Badge>
                      <span className="font-medium">{change.facetId}</span>
                      {change.kind === "metadata-changed" ? null : <span className="font-mono text-muted-foreground">{change.from ?? "—"} → {change.to ?? "—"}</span>}
                    </div>
                  ))}
                </div>
              ))}
            </div>
          );
        })}
        {current.length ? <p className="text-xs text-muted-foreground">On v{aspect.version}: {current.map((usage) => usage.definitionId).join(", ")}</p> : null}
        {byDefinition.length ? (
          <div className="flex justify-end">
            <Button type="button" onClick={apply} disabled={applying || selected.length === 0}><ArrowUpCircle className="h-4 w-4" />{applying ? "Upgrading…" : `Upgrade ${selected.length || ""} selected`}</Button>
          </div>
        ) : null}
      </CardContent>
    </Card>
  );
}
