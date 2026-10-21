import { useState } from "react";
import { Download, Eye, PackageOpen, Upload } from "lucide-react";
import { useOutletContext } from "react-router-dom";
import { toast } from "sonner";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Checkbox } from "@/components/ui/checkbox";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Switch } from "@/components/ui/switch";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { DiagnosticList } from "@/components/DiagnosticList";
import { TagInput } from "@/components/TagInput";
import { ErrorState, TableSkeleton } from "@/components/StateViews";
import { api, currentTenant, type CollisionMode, type ExportResultDto, type ExportScopeMode, type ExportVersionScope, type IdentityMapping, type ImportPreviewDto, type ImportResultDto, type PortableCounts, type PortableSnapshot } from "@/lib/api";
import { useAsync } from "@/hooks/useAsync";

type ShellContext = { reloadDefinitions: () => void; tenantName: string };

const scopeModes: Array<{ value: ExportScopeMode; label: string }> = [
  { value: "definition-with-resources", label: "Content types with their resources" },
  { value: "definitions-only", label: "Content types only" },
  { value: "selected-resources", label: "Selected resources" },
];

const versionScopes: Array<{ value: ExportVersionScope; label: string }> = [
  { value: "all-versions", label: "All versions" },
  { value: "latest-only", label: "Latest version only" },
  { value: "specific-versions", label: "Specific versions (id@version)" },
];

/** Export from the selected tenant and import into it (#112); switching tenants in between moves content across. */
export function Portability() {
  const { tenantName } = useOutletContext<ShellContext>();
  return (
    <div className="space-y-6">
      <div><h2 className="text-2xl font-semibold tracking-[-0.05em]">Portability</h2><p className="mt-1 text-sm text-muted-foreground">Snapshots carry definitions, the shared aspects they pin, resource versions, activation and lifecycle markers. To move content between tenants, export here, switch tenant, then preview and import.</p></div>
      <div className="grid gap-6 xl:grid-cols-2">
        <ExportCard tenantName={tenantName} />
        <ImportCard tenantName={tenantName} />
      </div>
    </div>
  );
}

function ExportCard({ tenantName }: { tenantName: string }) {
  const definitions = useAsync(api.getDefinitions, []);
  const [scopeMode, setScopeMode] = useState<ExportScopeMode>("definition-with-resources");
  const [definitionIds, setDefinitionIds] = useState<string[]>([]);
  const [resources, setResources] = useState<string[]>([]);
  const [versionScope, setVersionScope] = useState<ExportVersionScope>("all-versions");
  const [includeReferenced, setIncludeReferenced] = useState(false);
  const [exported, setExported] = useState<ExportResultDto | null>(null);
  const [busy, setBusy] = useState(false);

  const bySelectedResources = scopeMode === "selected-resources";
  const specific = bySelectedResources && versionScope === "specific-versions";

  function chooseScope(next: ExportScopeMode) {
    setScopeMode(next);
    if (next !== "selected-resources" && versionScope === "specific-versions") setVersionScope("all-versions");
  }

  async function runExport() {
    setBusy(true);
    try {
      // With specific versions, each entry names one version as "id@version".
      const versions = specific ? resources.map((entry) => { const at = entry.lastIndexOf("@"); return { resourceId: at > 0 ? entry.slice(0, at) : "", version: at > 0 ? Number(entry.slice(at + 1)) : Number.NaN }; }) : [];
      if (versions.some((entry) => !entry.resourceId || !Number.isInteger(entry.version) || entry.version < 1)) {
        toast.error("Export failed", { description: "Enter each resource as id@version, for example page-home@1." });
        return;
      }
      const result = await api.exportSnapshot({
        scopeMode,
        definitionIds: bySelectedResources ? [] : definitionIds,
        resourceIds: bySelectedResources && !specific ? resources : [],
        resourceVersionScope: versionScope,
        specificResourceVersions: versions,
        includeReferencedResources: includeReferenced,
      });
      setExported(result);
      if (result.result.snapshot) toast.success("Snapshot ready", { description: `${result.summary.resourceVersionCount} resource versions, ${result.summary.definitionCount} definition versions` });
      else toast.error("Export failed", { description: result.result.diagnostics.map((diagnostic) => diagnostic.code).join(", ") });
    } catch (error) {
      toast.error("Export failed", { description: error instanceof Error ? error.message : String(error) });
    } finally {
      setBusy(false);
    }
  }

  function download(snapshot: PortableSnapshot) {
    const url = URL.createObjectURL(new Blob([JSON.stringify(snapshot, null, 2)], { type: "application/json" }));
    const link = document.createElement("a");
    link.href = url;
    link.download = `aster-snapshot-${currentTenant()}-${new Date().toISOString().slice(0, 10)}.json`;
    link.click();
    setTimeout(() => URL.revokeObjectURL(url));
  }

  if (definitions.loading) return <TableSkeleton />;
  if (definitions.error || !definitions.data) return <ErrorState error={definitions.error} onRetry={definitions.reload} />;

  const summary = exported?.summary;
  const snapshot = exported?.result.snapshot;

  return (
    <Card>
      <CardHeader className="border-b border-border/60">
        <CardTitle className="flex items-center gap-2"><Download className="h-5 w-5 text-primary" />Export from {tenantName}</CardTitle>
        <CardDescription>Choose what to export, then download the snapshot.</CardDescription>
      </CardHeader>
      <CardContent className="space-y-4 pt-4">
        <div className="grid gap-3 sm:grid-cols-2">
          <div className="space-y-2"><Label>Scope</Label><Select value={scopeMode} onValueChange={(value) => chooseScope(value as ExportScopeMode)}><SelectTrigger aria-label="Export scope"><SelectValue /></SelectTrigger><SelectContent>{scopeModes.map((mode) => <SelectItem key={mode.value} value={mode.value}>{mode.label}</SelectItem>)}</SelectContent></Select></div>
          <div className="space-y-2"><Label>Versions</Label><Select value={versionScope} disabled={scopeMode === "definitions-only"} onValueChange={(value) => setVersionScope(value as ExportVersionScope)}><SelectTrigger aria-label="Versions"><SelectValue /></SelectTrigger><SelectContent>{versionScopes.filter((scope) => bySelectedResources || scope.value !== "specific-versions").map((scope) => <SelectItem key={scope.value} value={scope.value}>{scope.label}</SelectItem>)}</SelectContent></Select></div>
        </div>
        {bySelectedResources ? (
          <div className="space-y-2"><Label htmlFor="export-resources">Resources</Label><TagInput id="export-resources" value={resources} onChange={setResources} placeholder={specific ? "Add id@version and press Enter" : "Add a resource ID and press Enter"} /></div>
        ) : (
          <fieldset className="space-y-2"><legend className="text-sm font-medium">Content types</legend><div className="flex flex-wrap gap-3">{definitions.data.map((definition) => <label key={definition.definitionId} className="flex items-center gap-2 text-sm"><Checkbox aria-label={`Export ${definition.definitionId}`} checked={definitionIds.includes(definition.definitionId)} onCheckedChange={(checked) => setDefinitionIds((current) => checked === true ? [...current, definition.definitionId] : current.filter((id) => id !== definition.definitionId))} />{definition.displayName}</label>)}</div></fieldset>
        )}
        <label className="flex items-center gap-3 text-sm"><Switch aria-label="Include referenced resources" checked={includeReferenced} disabled={scopeMode === "definitions-only"} onCheckedChange={setIncludeReferenced} />Include resources that exported resources reference</label>
        <Button onClick={runExport} disabled={busy || (bySelectedResources ? resources.length === 0 : definitionIds.length === 0)}><PackageOpen className="h-4 w-4" />Export</Button>
        {exported ? (
          <section aria-label="Export result" className="space-y-3 rounded-lg border border-border/70 bg-background/35 p-4">
            {summary ? <p className="text-sm">{summary.definitionCount} definition versions · {summary.aspectDefinitionCount} shared aspect versions · {summary.resourceVersionCount} resource versions · {summary.activationEntryCount} activation entries · {summary.lifecycleMarkerCount} lifecycle markers{summary.skippedActivationEntryCount ? ` · ${summary.skippedActivationEntryCount} activation entries skipped` : ""}</p> : null}
            <DiagnosticList diagnostics={exported.result.diagnostics} />
            {snapshot ? <Button variant="outline" onClick={() => download(snapshot)}><Download className="h-4 w-4" />Download snapshot</Button> : null}
          </section>
        ) : null}
      </CardContent>
    </Card>
  );
}

function ImportCard({ tenantName }: { tenantName: string }) {
  const { reloadDefinitions } = useOutletContext<ShellContext>();
  const [snapshot, setSnapshot] = useState<PortableSnapshot | null>(null);
  const [collisionMode, setCollisionMode] = useState<CollisionMode>("strict");
  const [preview, setPreview] = useState<ImportPreviewDto | null>(null);
  const [result, setResult] = useState<ImportResultDto | null>(null);
  const [busy, setBusy] = useState(false);

  async function choose(file: File | undefined) {
    setSnapshot(null);
    setPreview(null);
    setResult(null);
    if (!file) return;
    try {
      setSnapshot(JSON.parse(await file.text()) as PortableSnapshot);
    } catch {
      toast.error("Not a snapshot", { description: `${file.name} is not valid JSON.` });
    }
  }

  async function run(write: boolean) {
    if (!snapshot) return;
    setBusy(true);
    try {
      if (!write) {
        setPreview(await api.previewImport(snapshot, collisionMode));
        setResult(null);
        return;
      }

      const imported = await api.importSnapshot(snapshot, collisionMode);
      setResult(imported);
      setPreview(null);
      reloadDefinitions();
      if (imported.result.status === "failed") toast.error("Import failed", { description: imported.result.diagnostics.map((diagnostic) => diagnostic.code).join(", ") });
      else toast.success(imported.result.status === "imported" ? `Imported into ${tenantName}` : "Nothing to import", { description: `${imported.summary.totalActualItemCount} items written` });
    } catch (error) {
      toast.error(write ? "Import failed" : "Preview failed", { description: error instanceof Error ? error.message : String(error) });
    } finally {
      setBusy(false);
    }
  }

  return (
    <Card>
      <CardHeader className="border-b border-border/60">
        <CardTitle className="flex items-center gap-2"><Upload className="h-5 w-5 text-primary" />Import into {tenantName}</CardTitle>
        <CardDescription>Preview first: it plans identity mappings against {tenantName} without writing. The import re-plans against current content and writes all or nothing.</CardDescription>
      </CardHeader>
      <CardContent className="space-y-4 pt-4">
        <div className="grid gap-3 sm:grid-cols-2">
          <div className="space-y-2"><Label htmlFor="snapshot-file">Snapshot file</Label><Input id="snapshot-file" type="file" accept="application/json,.json" onChange={(event) => choose(event.target.files?.[0])} /></div>
          <div className="space-y-2"><Label>Collisions</Label><Select value={collisionMode} onValueChange={(value) => { setCollisionMode(value as CollisionMode); setPreview(null); }}><SelectTrigger aria-label="Collisions"><SelectValue /></SelectTrigger><SelectContent><SelectItem value="strict">Strict: fail on divergent content</SelectItem><SelectItem value="remap-divergent">Remap divergent content</SelectItem></SelectContent></Select></div>
        </div>
        {snapshot ? <p className="text-sm text-muted-foreground">From tenant <span className="font-mono">{snapshot.sourceTenantScope?.tenantId ?? "default"}</span> · {snapshot.definitions?.length ?? 0} definition versions · {snapshot.resources?.length ?? 0} resource versions</p> : null}
        <div className="flex flex-wrap gap-2">
          <Button variant="outline" disabled={busy || !snapshot} onClick={() => run(false)}><Eye className="h-4 w-4" />Preview import</Button>
          <Button disabled={busy || !preview?.result.canImport} onClick={() => run(true)}><Upload className="h-4 w-4" />Import into {tenantName}</Button>
        </div>
        {preview ? (
          <section aria-label="Import preview" className="space-y-3 rounded-lg border border-border/70 bg-background/35 p-4">
            <div className="flex items-center gap-2"><h3 className="text-sm font-medium">{preview.result.canImport ? "Ready to import" : "Cannot import"}</h3><Badge variant={preview.result.canImport ? "schema" : "destructive"}>{preview.summary.totalPlannedItemCount} planned</Badge></div>
            <ImportDetails counts={preview.result.counts} identityMap={preview.result.identityMap} />
            <DiagnosticList diagnostics={preview.result.diagnostics} />
          </section>
        ) : null}
        {result ? (
          <section aria-label="Import result" className="space-y-3 rounded-lg border border-border/70 bg-background/35 p-4">
            <div className="flex items-center gap-2"><h3 className="text-sm font-medium">{result.result.status === "imported" ? "Imported" : result.result.status === "no-op" ? "Already up to date" : "Import failed"}</h3><Badge variant={result.result.status === "failed" ? "destructive" : "schema"}>{result.result.status}</Badge></div>
            <ImportDetails counts={result.result.counts} identityMap={result.result.identityMap} />
            <DiagnosticList diagnostics={result.result.diagnostics} />
          </section>
        ) : null}
      </CardContent>
    </Card>
  );
}

/** Snapshot identities are JSON arrays such as ["page",1]; versions read "v1". */
function formatIdentity(id: string) {
  try {
    const parts: unknown = JSON.parse(id);
    if (Array.isArray(parts)) return parts.map((part) => (typeof part === "number" ? `v${part}` : String(part))).join(" ");
  } catch {
    // A plain resource ID.
  }
  return id;
}

function ImportDetails({ counts, identityMap }: { counts: PortableCounts; identityMap: IdentityMapping[] }) {
  const mappings = identityMap.filter((mapping) => mapping.entityKind !== "reference-value");
  const rewrites = identityMap.filter((mapping) => mapping.entityKind === "reference-value");
  return (
    <>
      <p className="text-sm text-muted-foreground">{counts.definitions} definition versions · {counts.aspectDefinitions} shared aspect versions · {counts.resources} resources ({counts.resourceVersions} versions) · {counts.activationEntries} activation entries · {counts.lifecycleMarkers} lifecycle markers · {counts.reusedIdenticalItems} reused · {counts.remappedItems} remapped</p>
      {mappings.length ? (
        <Table aria-label="Identity mappings">
          <TableHeader><TableRow><TableHead>Kind</TableHead><TableHead>Source</TableHead><TableHead>Target</TableHead><TableHead>Reason</TableHead></TableRow></TableHeader>
          <TableBody>{mappings.map((mapping, index) => <TableRow key={index}><TableCell className="text-xs text-muted-foreground">{mapping.entityKind}</TableCell><TableCell className="font-mono text-xs">{formatIdentity(mapping.sourceId)}</TableCell><TableCell className="font-mono text-xs">{formatIdentity(mapping.targetId)}</TableCell><TableCell><Badge variant={mapping.reason === "remapped-divergent" ? "preview" : mapping.reason === "collided-divergent" ? "destructive" : "outline"}>{mapping.reason}</Badge></TableCell></TableRow>)}</TableBody>
        </Table>
      ) : null}
      {rewrites.length ? (
        <Table aria-label="Reference rewrites">
          <TableHeader><TableRow><TableHead>Reference value</TableHead><TableHead>From</TableHead><TableHead>To</TableHead></TableRow></TableHeader>
          <TableBody>{rewrites.map((rewrite, index) => <TableRow key={index}><TableCell className="font-mono text-xs">{rewrite.location}</TableCell><TableCell className="font-mono text-xs">{rewrite.sourceId}</TableCell><TableCell className="font-mono text-xs">{rewrite.targetId}</TableCell></TableRow>)}</TableBody>
        </Table>
      ) : null}
    </>
  );
}
