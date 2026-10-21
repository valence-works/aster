import { useState } from "react";
import { Archive, Play, Scissors, ShieldCheck } from "lucide-react";
import { Link } from "react-router-dom";
import { toast } from "sonner";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Checkbox } from "@/components/ui/checkbox";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { DiagnosticList } from "@/components/DiagnosticList";
import { ErrorState, TableSkeleton } from "@/components/StateViews";
import { api, type CodedDiagnostic, type PolicyCandidateDto, type PolicyDeclarationDto, type PolicyPreviewDto } from "@/lib/api";
import { formatDate } from "@/lib/utils";
import { useAsync } from "@/hooks/useAsync";

type AppliedRow = { key: string; resourceId: string | null; resourceVersion: number | null; status: string; diagnostics: CodedDiagnostic[] };
type Application = { title: string; rows: AppliedRow[] };

const candidateKey = (candidate: PolicyCandidateDto) => `${candidate.policyId}|${candidate.resourceId}|${candidate.resourceVersion ?? ""}`;
const isLifecycleOutcome = (candidate: PolicyCandidateDto) => candidate.outcome === "archive" || candidate.outcome === "soft-delete";

/** A .NET TimeSpan ("365.00:00:00") as days or hours. */
function formatAge(value: string) {
  const match = /^(?:(\d+)\.)?(\d+):(\d+):(\d+)/.exec(value);
  if (!match) return value;
  const days = Number(match[1] ?? 0);
  return days ? `${days} day${days === 1 ? "" : "s"}` : `${Number(match[2])} hours`;
}

function describeCriteria({ criteria }: PolicyDeclarationDto): string[] {
  return [
    criteria.activationState === "draft" ? "latest version never activated" : null,
    criteria.activationState === "active" ? `active on ${criteria.activationChannel}` : null,
    criteria.lifecycleState ? (criteria.lifecycleState === "none" ? "not archived or soft-deleted" : criteria.lifecycleState) : null,
    criteria.minimumAge ? `older than ${formatAge(criteria.minimumAge)}` : null,
    criteria.maximumRetainedVersions ? `keeps the newest ${criteria.maximumRetainedVersions}` : null,
  ].filter((part): part is string => part !== null);
}

/** Policy declarations on a definition, their preview, and applying selected outcomes (#111). */
export function DefinitionPoliciesPanel({ definitionId }: { definitionId: string }) {
  const policies = useAsync(() => api.getPolicies(definitionId), [definitionId]);
  const [asOf, setAsOf] = useState("");
  const [preview, setPreview] = useState<PolicyPreviewDto | null>(null);
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [application, setApplication] = useState<Application | null>(null);
  const [busy, setBusy] = useState(false);

  async function runPreview() {
    setBusy(true);
    try {
      setPreview(await api.previewPolicies(definitionId, asOf ? `${asOf}T00:00:00Z` : null));
      setSelected(new Set());
    } catch (error) {
      toast.error("Preview failed", { description: error instanceof Error ? error.message : String(error) });
    } finally {
      setBusy(false);
    }
  }

  async function apply(kind: "lifecycle" | "prune") {
    if (!preview) return;
    const chosen = preview.candidates.filter((candidate) => selected.has(candidateKey(candidate)) && (kind === "lifecycle") === isLifecycleOutcome(candidate));
    setBusy(true);
    try {
      if (kind === "lifecycle") {
        const result = await api.applyPolicyOutcomes(chosen);
        setApplication({ title: `Applied ${result.summary.appliedCount} of ${result.summary.totalCount}`, rows: result.candidates.map((row) => ({ key: `${row.index}`, ...row })) });
        notify(result.summary.failedCount, `${result.summary.appliedCount} applied, ${result.summary.alreadySatisfiedCount} already satisfied`);
      } else {
        const result = await api.pruneVersions(chosen);
        setApplication({ title: `Pruned ${result.summary.prunedCount} of ${result.summary.totalCount}`, rows: result.candidates.map((row) => ({ key: `${row.index}`, ...row })) });
        notify(result.summary.failedCount, `${result.summary.prunedCount} pruned, ${result.summary.alreadyPrunedCount} already pruned`);
      }
    } catch (error) {
      toast.error("Application failed", { description: error instanceof Error ? error.message : String(error) });
    } finally {
      setBusy(false);
    }
    await runPreview();
  }

  function toggle(candidate: PolicyCandidateDto, checked: boolean) {
    setSelected((current) => {
      const next = new Set(current);
      if (checked) next.add(candidateKey(candidate));
      else next.delete(candidateKey(candidate));
      return next;
    });
  }

  if (policies.loading) return <TableSkeleton />;
  if (policies.error || !policies.data) return <ErrorState error={policies.error} onRetry={policies.reload} />;

  const declarations = policies.data.policies;
  const selectedCandidates = preview?.candidates.filter((candidate) => selected.has(candidateKey(candidate))) ?? [];
  const lifecycleCount = selectedCandidates.filter(isLifecycleOutcome).length;
  const pruneCount = selectedCandidates.length - lifecycleCount;

  return (
    <Card>
      <CardHeader className="border-b border-border/60">
        <CardTitle className="flex items-center gap-2"><ShieldCheck className="h-5 w-5 text-primary" />Policies</CardTitle>
        <CardDescription>Declared on definition v{policies.data.definitionVersion}. Nothing runs on its own: preview, pick outcomes, then apply them. Application re-checks each one against current state.</CardDescription>
      </CardHeader>
      <CardContent className="space-y-5 pt-4">
        {declarations.length === 0 ? <p className="text-sm text-muted-foreground">This definition declares no policies.</p> : (
          <Table aria-label="Policy declarations">
            <TableHeader><TableRow><TableHead>Policy</TableHead><TableHead>Kind</TableHead><TableHead>Outcome</TableHead><TableHead>Criteria</TableHead></TableRow></TableHeader>
            <TableBody>{declarations.map((policy) => <TableRow key={policy.policyId}><TableCell><div className="font-medium">{policy.name ?? policy.policyId}</div><div className="font-mono text-xs text-muted-foreground">{policy.policyId}</div></TableCell><TableCell><Badge variant="schema">{policy.kind}</Badge></TableCell><TableCell><Badge variant="facet">{policy.outcome}</Badge></TableCell><TableCell className="text-sm text-muted-foreground">{describeCriteria(policy).join(" · ") || "—"}</TableCell></TableRow>)}</TableBody>
          </Table>
        )}
        <DiagnosticList diagnostics={policies.data.diagnostics} />
        {declarations.length ? (
          <div className="flex flex-wrap items-end gap-3">
            <div className="space-y-2"><Label htmlFor="policy-as-of">Evaluate as of</Label><Input id="policy-as-of" type="date" className="w-44" value={asOf} onChange={(event) => setAsOf(event.target.value)} /></div>
            <Button onClick={runPreview} disabled={busy}><Play className="h-4 w-4" />Run preview</Button>
            {preview ? <span className="pb-2 text-xs text-muted-foreground">Evaluated at {formatDate(preview.summary.evaluationTimestamp ?? undefined)} · {preview.summary.totalCandidateCount} candidates</span> : null}
          </div>
        ) : null}
        {preview ? (
          <div className="space-y-3">
            <DiagnosticList diagnostics={preview.diagnostics} />
            {preview.candidates.length === 0 ? <p className="text-sm text-muted-foreground">No resources match these policies.</p> : (
              <Table aria-label="Policy candidates">
                <TableHeader><TableRow><TableHead className="w-10"><span className="sr-only">Select</span></TableHead><TableHead>Policy</TableHead><TableHead>Resource</TableHead><TableHead>Outcome</TableHead></TableRow></TableHeader>
                <TableBody>
                  {preview.candidates.map((candidate) => {
                    const key = candidateKey(candidate);
                    const selectable = isLifecycleOutcome(candidate) || candidate.outcome === "prune-preview";
                    return (
                      <TableRow key={key}>
                        <TableCell><Checkbox aria-label={`Select ${candidate.policyId} for ${candidate.resourceId} v${candidate.resourceVersion}`} checked={selected.has(key)} disabled={!selectable} onCheckedChange={(checked) => toggle(candidate, checked === true)} /></TableCell>
                        <TableCell className="font-mono text-xs">{candidate.policyId}</TableCell>
                        <TableCell><Link className="font-medium hover:underline" to={`/content/${definitionId}/${encodeURIComponent(candidate.resourceId)}`}>{candidate.resourceId}</Link><div className="text-xs text-muted-foreground">v{candidate.resourceVersion}</div></TableCell>
                        <TableCell><Badge variant="facet">{candidate.outcome}</Badge></TableCell>
                      </TableRow>
                    );
                  })}
                </TableBody>
              </Table>
            )}
            <div className="flex flex-wrap gap-2">
              <Button variant="outline" disabled={busy || lifecycleCount === 0} onClick={() => apply("lifecycle")}><Archive className="h-4 w-4" />Apply {lifecycleCount} archive/soft-delete</Button>
              <Button variant="outline" disabled={busy || pruneCount === 0} onClick={() => apply("prune")}><Scissors className="h-4 w-4" />Prune {pruneCount} versions</Button>
            </div>
          </div>
        ) : null}
        {application ? (
          <section aria-label="Application result" className="space-y-2 rounded-lg border border-border/70 bg-background/35 p-4">
            <h3 className="text-sm font-medium">{application.title}</h3>
            {application.rows.map((row) => <div key={row.key} role="group" aria-label={`${row.resourceId} v${row.resourceVersion}`} className="space-y-1.5"><div className="flex items-center gap-2 text-sm"><span className="font-mono">{row.resourceId} v{row.resourceVersion}</span><Badge variant={row.status === "failed" ? "destructive" : "schema"}>{row.status}</Badge></div><DiagnosticList diagnostics={row.diagnostics} /></div>)}
          </section>
        ) : null}
      </CardContent>
    </Card>
  );
}

function notify(failedCount: number, description: string) {
  if (failedCount) toast.error(`${failedCount} failed`, { description });
  else toast.success("Policy outcomes applied", { description });
}
