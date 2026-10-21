import { useState } from "react";
import { Archive, ArrowUpCircle, RotateCcw, Trash2, Wrench } from "lucide-react";
import { toast } from "sonner";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { DiagnosticList } from "@/components/DiagnosticList";
import { LifecycleBadge } from "@/components/LifecycleBadge";
import { ErrorState, TableSkeleton } from "@/components/StateViews";
import { ApiError, api, type CodedDiagnostic, type LifecycleState, type RestoreCandidateDto, type SchemaStatusDto, type SchemaUpgradeResultDto } from "@/lib/api";
import { formatDate } from "@/lib/utils";
import { useAsync } from "@/hooks/useAsync";

type MarkState = Exclude<LifecycleState, "none">;

const errorDiagnostics = (error: unknown): CodedDiagnostic[] =>
  error instanceof ApiError && error.diagnostics ? error.diagnostics : [{ code: "request-failed", message: error instanceof Error ? error.message : String(error) }];

/**
 * Lifecycle markers and schema status for one resource, through Studio's operations endpoints (#111).
 * `editedAgainst` is the definition version the editor's form is rendered from (#191).
 */
export function ResourceOperationsPanel({ resourceId, latestVersion, editedAgainst, onChanged }: { resourceId: string; latestVersion: number; editedAgainst: number; onChanged: () => void }) {
  const state = useAsync(async () => ({ lifecycle: await api.getLifecycle(resourceId), schema: await api.getSchemaStatus(resourceId) }), [resourceId, latestVersion]);
  // Kept here: an upgrade appends a version, which reloads the sections below.
  const [lastUpgrade, setLastUpgrade] = useState<SchemaUpgradeResultDto | null>(null);

  if (state.loading) return <TableSkeleton />;
  if (state.error || !state.data) return <ErrorState error={state.error} onRetry={state.reload} />;

  const { lifecycle, schema } = state.data;
  return (
    <Card>
      <CardHeader><CardTitle className="flex items-center gap-2"><Wrench className="h-5 w-5 text-primary" />Operations</CardTitle><CardDescription>Lifecycle markers sit beside the immutable versions; a schema upgrade appends one.</CardDescription></CardHeader>
      <CardContent className="space-y-5">
        <LifecycleSection resourceId={resourceId} state={lifecycle.state} markedAt={lifecycle.markedAt} reason={lifecycle.reason} onChanged={state.reload} />
        <SchemaSection schema={schema} editedAgainst={editedAgainst} lastUpgrade={lastUpgrade} onUpgraded={(result) => { setLastUpgrade(result); onChanged(); }} />
      </CardContent>
    </Card>
  );
}

function LifecycleSection({ resourceId, state, markedAt, reason, onChanged }: { resourceId: string; state: LifecycleState; markedAt: string | null; reason: string | null; onChanged: () => void }) {
  const [marking, setMarking] = useState<MarkState | null>(null);
  const [restoring, setRestoring] = useState(false);

  return (
    <section aria-label="Lifecycle" className="space-y-3">
      <div className="flex items-center justify-between gap-2"><h3 className="text-sm font-medium">Lifecycle</h3><LifecycleBadge state={state} /></div>
      {state !== "none" ? <p className="text-xs text-muted-foreground">Marked {formatDate(markedAt ?? undefined)}{reason ? ` · ${reason}` : ""}</p> : null}
      <div className="flex flex-wrap gap-2">
        {state === "none" ? (
          <>
            <Button size="sm" variant="outline" onClick={() => setMarking("archived")}><Archive className="h-4 w-4" />Archive</Button>
            <Button size="sm" variant="outline" onClick={() => setMarking("soft-deleted")}><Trash2 className="h-4 w-4" />Soft-delete</Button>
          </>
        ) : <Button size="sm" variant="outline" onClick={() => setRestoring(true)}><RotateCcw className="h-4 w-4" />Restore</Button>}
      </div>
      {marking ? <MarkDialog resourceId={resourceId} state={marking} onClose={() => setMarking(null)} onMarked={onChanged} /> : null}
      {restoring && state !== "none" ? <RestoreDialog resourceId={resourceId} expectedState={state} onClose={() => setRestoring(false)} onRestored={onChanged} /> : null}
    </section>
  );
}

function MarkDialog({ resourceId, state, onClose, onMarked }: { resourceId: string; state: MarkState; onClose: () => void; onMarked: () => void }) {
  const [reason, setReason] = useState("");
  const [busy, setBusy] = useState(false);
  const [diagnostics, setDiagnostics] = useState<CodedDiagnostic[]>([]);
  const verb = state === "archived" ? "Archive" : "Soft-delete";

  async function mark() {
    setBusy(true);
    try {
      await api.markResource(resourceId, state, reason);
      toast.success(`${resourceId} is ${state}`, { description: "Restore clears the marker; no version was rewritten." });
      onMarked();
      onClose();
    } catch (error) {
      setDiagnostics(errorDiagnostics(error));
    } finally {
      setBusy(false);
    }
  }

  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent>
        <DialogHeader><DialogTitle>{verb} {resourceId}</DialogTitle><DialogDescription>Writes a lifecycle marker through IResourceLifecycleMarkerService. Versions and channel activation stay as they are.</DialogDescription></DialogHeader>
        <div className="space-y-2"><Label htmlFor="marker-reason">Reason</Label><Input id="marker-reason" value={reason} onChange={(event) => setReason(event.target.value)} placeholder="Optional, shown with the marker" /></div>
        <DiagnosticList diagnostics={diagnostics} />
        <DialogFooter><Button variant="outline" onClick={onClose}>Cancel</Button><Button onClick={mark} disabled={busy}>{busy ? "Saving…" : verb}</Button></DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

function RestoreDialog({ resourceId, expectedState, onClose, onRestored }: { resourceId: string; expectedState: MarkState; onClose: () => void; onRestored: () => void }) {
  // The restore applies the same candidate the preview checked: this resource, expected to be in the state shown.
  const preview = useAsync(() => api.previewRestore(resourceId, expectedState), [resourceId, expectedState]);
  const [busy, setBusy] = useState(false);
  const [outcome, setOutcome] = useState<RestoreCandidateDto | null>(null);
  const candidate = outcome ?? preview.data?.candidates[0];

  async function restore() {
    setBusy(true);
    try {
      const result = await api.restoreResource(resourceId, expectedState);
      const restored = result.candidates[0];
      if (restored.status === "restored" || restored.status === "already-restored") {
        toast.success(`${resourceId} restored`, { description: `The ${expectedState} marker was cleared.` });
        onRestored();
        onClose();
      } else {
        setOutcome(restored);
      }
    } catch (error) {
      toast.error("Restore failed", { description: error instanceof Error ? error.message : String(error) });
    } finally {
      setBusy(false);
    }
  }

  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent>
        <DialogHeader><DialogTitle>Restore {resourceId}</DialogTitle><DialogDescription>Previewed through IResourceLifecycleRestoreService; restoring clears the marker only if it is still {expectedState}.</DialogDescription></DialogHeader>
        {preview.loading ? <TableSkeleton /> : preview.error ? <ErrorState error={preview.error} onRetry={preview.reload} /> : candidate ? (
          <div className="space-y-3">
            <div className="flex items-center gap-2 text-sm"><span className="text-muted-foreground">{outcome ? "Restore" : "Preview"}:</span><Badge variant={candidate.status === "failed" ? "destructive" : "schema"}>{candidate.status}</Badge></div>
            <DiagnosticList diagnostics={candidate.diagnostics} />
          </div>
        ) : null}
        <DialogFooter><Button variant="outline" onClick={onClose}>Cancel</Button><Button onClick={restore} disabled={busy || candidate?.status !== "restorable"}><RotateCcw className="h-4 w-4" />{busy ? "Restoring…" : "Restore"}</Button></DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

const schemaLabels: Record<SchemaStatusDto["status"], string> = {
  current: "current",
  "older-than-latest": "upgrade available",
  "missing-definition": "definition missing",
  "missing-definition-version": "definition version missing",
  "unknown-resource-lineage": "unknown lineage",
};

function SchemaSection({ schema, editedAgainst, lastUpgrade, onUpgraded }: { schema: SchemaStatusDto; editedAgainst: number; lastUpgrade: SchemaUpgradeResultDto | null; onUpgraded: (result: SchemaUpgradeResultDto) => void }) {
  const [confirming, setConfirming] = useState(false);
  const [busy, setBusy] = useState(false);
  const [diagnostics, setDiagnostics] = useState<CodedDiagnostic[]>([]);
  const keys = schema.undeclaredAspectKeys;

  async function upgrade() {
    setBusy(true);
    setDiagnostics([]);
    try {
      const upgraded = await api.upgradeSchema(schema.resourceId, schema.resourceVersion);
      setConfirming(false);
      toast.success(upgraded.message);
      onUpgraded(upgraded);
    } catch (error) {
      setDiagnostics(errorDiagnostics(error));
    } finally {
      setBusy(false);
    }
  }

  return (
    <section aria-label="Schema" className="space-y-3 border-t border-border/60 pt-4">
      <div className="flex items-center justify-between gap-2"><h3 className="text-sm font-medium">Schema</h3><Badge variant={schema.status === "current" ? "schema" : "preview"}>{schemaLabels[schema.status]}</Badge></div>
      <p className="text-xs text-muted-foreground">v{schema.resourceVersion} was saved against definition v{schema.recordedDefinitionVersion ?? "?"}; the latest is v{schema.latestDefinitionVersion ?? "?"}.</p>
      {keys.length ? <p className="text-xs text-muted-foreground">{schema.status === "older-than-latest" ? "Carried forward on upgrade" : "Carried forward, not declared"}: {keys.map((key) => <Badge key={key} variant="facet" className="mr-1">{key}</Badge>)}</p> : null}
      {lastUpgrade ? <p className="text-xs text-muted-foreground">Last upgrade: {lastUpgrade.status}, definition v{lastUpgrade.sourceDefinitionVersion ?? "?"} → v{lastUpgrade.targetDefinitionVersion}, carried forward {lastUpgrade.carriedForwardAspectKeys.length ? lastUpgrade.carriedForwardAspectKeys.join(", ") : "nothing"}.</p> : null}
      {schema.latestDefinitionVersion !== null && editedAgainst < schema.latestDefinitionVersion ? <p role="status" className="text-xs font-medium">Edited against v{editedAgainst}; v{schema.latestDefinitionVersion} available. The form and the save both use v{editedAgainst} until you upgrade.</p> : null}
      {schema.status === "older-than-latest" ? <Button size="sm" variant="outline" onClick={() => setConfirming(true)}><ArrowUpCircle className="h-4 w-4" />Upgrade to definition v{schema.latestDefinitionVersion}</Button> : null}
      <Dialog open={confirming} onOpenChange={(open) => { if (!open) { setConfirming(false); setDiagnostics([]); } }}>
        <DialogContent>
          <DialogHeader><DialogTitle>Upgrade {schema.resourceId}</DialogTitle><DialogDescription>Appends v{schema.resourceVersion + 1} recorded against definition v{schema.latestDefinitionVersion}, based on v{schema.resourceVersion}. The SDK has no upgrade preview; the aspects below are what the latest definition doesn't declare.</DialogDescription></DialogHeader>
          <p className="text-sm">{keys.length ? <>Carried forward: {keys.map((key) => <Badge key={key} variant="facet" className="mr-1">{key}</Badge>)}</> : "No aspects will be carried forward."}</p>
          <DiagnosticList diagnostics={diagnostics} />
          <DialogFooter><Button variant="outline" onClick={() => setConfirming(false)}>Cancel</Button><Button onClick={upgrade} disabled={busy}>{busy ? "Upgrading…" : "Upgrade"}</Button></DialogFooter>
        </DialogContent>
      </Dialog>
    </section>
  );
}
