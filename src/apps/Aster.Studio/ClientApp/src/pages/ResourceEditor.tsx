import { useEffect, useState } from "react";
import { ArrowLeft, Save } from "lucide-react";
import { Link, useNavigate, useParams } from "react-router-dom";
import { toast } from "sonner";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { DynamicResourceForm } from "@/components/DynamicResourceForm";
import { VersionHistoryPanel } from "@/components/VersionHistoryPanel";
import { ErrorState, TableSkeleton } from "@/components/StateViews";
import { ApiError, api, type ResourceAspects, type ResourceDetailDto } from "@/lib/api";
import { diagnosticsToFieldErrors, initialAspects, normalizeForSubmit, validateAspects, type FieldErrors } from "@/lib/resourceForm";
import { useAsync } from "@/hooks/useAsync";

export function ResourceEditor({ mode }: { mode: "create" | "edit" }) {
  const { definitionId = "", resourceId = "" } = useParams();
  const navigate = useNavigate();
  const definition = useAsync(() => api.getDefinition(definitionId), [definitionId]);
  const resource = useAsync<ResourceDetailDto | null>(() => mode === "edit" ? api.getResource(resourceId) : Promise.resolve(null), [mode, resourceId]);
  const [aspects, setAspects] = useState<ResourceAspects>({});
  const [errors, setErrors] = useState<FieldErrors>({});
  const [formErrors, setFormErrors] = useState<string[]>([]);
  const [saving, setSaving] = useState(false);

  useEffect(() => {
    if (definition.data && !resource.loading) setAspects(initialAspects(definition.data, resource.data));
  }, [definition.data, resource.data, resource.loading]);

  if (definition.loading || resource.loading) return <TableSkeleton />;
  if (definition.error || !definition.data) return <ErrorState error={definition.error} onRetry={definition.reload} />;
  if (resource.error) return <ErrorState error={resource.error} onRetry={resource.reload} />;

  async function submit() {
    if (!definition.data) return;
    const normalized = normalizeForSubmit(definition.data, aspects);
    setAspects(normalized);
    const clientErrors = validateAspects(definition.data, normalized);
    setErrors(clientErrors);
    setFormErrors([]);
    if (Object.keys(clientErrors).length) {
      toast.error("Fix highlighted fields", { description: "Client-side validation mirrors facet metadata." });
      return;
    }
    setSaving(true);
    try {
      if (mode === "create") {
        const created = await api.createResource({ definitionId, aspects: normalized });
        toast.success(`${definition.data.displayName} created`, { description: `Immutable v${created.version} saved.` });
        navigate(`/content/${definitionId}/${created.resourceId}`);
      } else if (resource.data) {
        const updated = await api.updateResource(resourceId, { baseVersion: resource.data.version, aspects: normalized });
        toast.success(`Saved v${updated.version}`, { description: "A new immutable version was appended." });
        resource.setData(updated);
      }
    } catch (error) {
      if (error instanceof ApiError && error.status === 400 && error.diagnostics) {
        const mapped = diagnosticsToFieldErrors(error.diagnostics);
        setErrors(mapped.fieldErrors);
        setFormErrors(mapped.formErrors);
        toast.error("Server validation failed", { description: "Diagnostics were mapped back onto generated fields." });
      } else if (error instanceof ApiError && error.status === 409) {
        toast.error("Version conflict", { description: error.message, action: { label: "Reload", onClick: resource.reload } });
      } else {
        toast.error("Save failed", { description: error instanceof Error ? error.message : String(error) });
      }
    } finally {
      setSaving(false);
    }
  }

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div className="flex items-start gap-3"><Button variant="outline" size="icon" asChild><Link to={`/content/${definitionId}`}><ArrowLeft className="h-4 w-4" /></Link></Button><div><h2 className="text-2xl font-semibold tracking-[-0.05em]">{mode === "create" ? `New ${definition.data.displayName}` : resource.data?.resourceId}</h2><p className="mt-1 text-sm text-muted-foreground">Generated from definition <span className="font-mono">{definitionId}</span>. Every card below is an attached aspect.</p></div></div>
        <div className="flex items-center gap-2">{resource.data ? <Badge variant="schema">base v{resource.data.version}</Badge> : null}<Button onClick={submit} disabled={saving}><Save className="h-4 w-4" />{saving ? "Saving…" : mode === "create" ? "Create resource" : "Save new version"}</Button></div>
      </div>
      {formErrors.length ? <Card className="border-destructive/30 bg-destructive/5"><CardContent className="space-y-1 p-4 text-sm text-destructive">{formErrors.map((error) => <div key={error}>{error}</div>)}</CardContent></Card> : null}
      <div className="grid gap-6 xl:grid-cols-[minmax(0,1fr)_380px]">
        <DynamicResourceForm definition={definition.data} aspects={aspects} errors={errors} onChange={setAspects} />
        <aside className="space-y-4 xl:sticky xl:top-24 xl:self-start">
          <Card className="facet-seam"><CardContent className="p-5"><h3 className="font-semibold tracking-tight">Dynamic form contract</h3><p className="mt-2 text-sm leading-6 text-muted-foreground">Widgets are selected from facet metadata: uiHint, type, allowed values, and constraints. Server diagnostics are routed by aspectKey + facetId.</p></CardContent></Card>
          {mode === "edit" && resource.data ? <VersionHistoryPanel resourceId={resourceId} latestVersion={resource.data.version} definition={definition.data} onChanged={resource.reload} /> : null}
        </aside>
      </div>
    </div>
  );
}
