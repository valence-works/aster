import { useState } from "react";
import { CheckCircle2, Plus, RefreshCw } from "lucide-react";
import { Link, useOutletContext } from "react-router-dom";
import { toast } from "sonner";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Checkbox } from "@/components/ui/checkbox";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle, DialogTrigger } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Switch } from "@/components/ui/switch";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { ConstraintBadges } from "@/components/FacetBadges";
import { EmptyState, ErrorState, TableSkeleton } from "@/components/StateViews";
import { ApiError, api, type AspectTemplateDto, type DiagnosticDto } from "@/lib/api";
import { toKebabCase } from "@/lib/utils";
import { useAsync } from "@/hooks/useAsync";

type ShellContext = { reloadDefinitions: () => void };

export function ContentTypes() {
  const definitions = useAsync(api.getDefinitions, []);
  const templates = useAsync(api.getAspectTemplates, []);
  const { reloadDefinitions } = useOutletContext<ShellContext>();

  if (definitions.loading) return <TableSkeleton />;
  if (definitions.error) return <ErrorState error={definitions.error} onRetry={definitions.reload} />;

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div><h2 className="text-2xl font-semibold tracking-[-0.05em]">Content type definitions</h2><p className="mt-1 text-sm text-muted-foreground">Definitions compose horizontal aspects into CMS-ready schemas.</p></div>
        <NewContentTypeDialog templates={templates.data ?? []} templatesLoading={templates.loading} onCreated={() => { definitions.reload(); reloadDefinitions(); }} />
      </div>
      <Card>
        <CardContent className="p-0">
          {!definitions.data?.length ? <div className="p-6"><EmptyState title="No content types" description="Create a definition from reusable aspect templates to start modelling content." /></div> : (
            <Table>
              <TableHeader><TableRow><TableHead>Name</TableHead><TableHead>Version</TableHead><TableHead>Aspects</TableHead><TableHead>Items</TableHead><TableHead>Mode</TableHead></TableRow></TableHeader>
              <TableBody>{definitions.data.map((definition) => <TableRow key={definition.definitionId} className="cursor-pointer"><TableCell><Link to={`/content-types/${definition.definitionId}`} className="font-medium hover:text-primary">{definition.displayName}</Link><div className="text-xs text-muted-foreground">{definition.definitionId}</div></TableCell><TableCell className="font-mono">v{definition.version}</TableCell><TableCell>{definition.aspectCount}</TableCell><TableCell>{definition.resourceCount}</TableCell><TableCell><Badge variant={definition.isSingleton ? "preview" : "secondary"}>{definition.isSingleton ? "singleton" : "collection"}</Badge></TableCell></TableRow>)}</TableBody>
            </Table>
          )}
        </CardContent>
      </Card>
    </div>
  );
}

function NewContentTypeDialog({ templates, templatesLoading, onCreated }: { templates: AspectTemplateDto[]; templatesLoading: boolean; onCreated: () => void }) {
  const [open, setOpen] = useState(false);
  const [definitionId, setDefinitionId] = useState("");
  const [isSingleton, setIsSingleton] = useState(false);
  const [selected, setSelected] = useState<string[]>([]);
  const [diagnostics, setDiagnostics] = useState<DiagnosticDto[]>([]);
  const [saving, setSaving] = useState(false);

  async function submit() {
    setSaving(true);
    setDiagnostics([]);
    try {
      const cleanedId = toKebabCase(definitionId);
      await api.createDefinition({
        definitionId: cleanedId,
        isSingleton,
        aspects: selected.map((key) => ({ aspectKey: key, templateKey: key })),
      });
      toast.success(`${cleanedId} content type created`, { description: "Aspect templates copied into the definition." });
      setOpen(false);
      setDefinitionId("");
      setSelected([]);
      setIsSingleton(false);
      onCreated();
    } catch (error) {
      if (error instanceof ApiError && error.diagnostics) setDiagnostics(error.diagnostics);
      toast.error("Could not create content type", { description: error instanceof Error ? error.message : String(error) });
    } finally {
      setSaving(false);
    }
  }

  return (
    <Dialog open={open} onOpenChange={setOpen}>
      <DialogTrigger asChild><Button><Plus className="h-4 w-4" />New content type</Button></DialogTrigger>
      <DialogContent className="max-w-4xl">
        <DialogHeader><DialogTitle>New content type</DialogTitle><DialogDescription>Give the definition a kebab-case id, then choose aspect templates. Each selected aspect contributes its facets, UI metadata, and validation.</DialogDescription></DialogHeader>
        <div className="grid gap-5 lg:grid-cols-[280px_1fr]">
          <div className="space-y-4">
            <div className="space-y-2"><Label htmlFor="definitionId">Definition id</Label><Input id="definitionId" value={definitionId} onChange={(event) => setDefinitionId(toKebabCase(event.target.value))} placeholder="campaign-page" /><p className="text-xs text-muted-foreground">Stored as {definitionId || "kebab-case-id"}</p></div>
            <div className="flex items-center justify-between rounded-md border border-border/70 bg-secondary/35 p-3"><div><Label>Singleton</Label><p className="text-xs text-muted-foreground">Use for global settings or one-off pages.</p></div><Switch checked={isSingleton} onCheckedChange={setIsSingleton} /></div>
            {diagnostics.length ? <div className="rounded-md border border-destructive/30 bg-destructive/10 p-3 text-sm text-destructive">{diagnostics.map((diagnostic) => <div key={`${diagnostic.code}-${diagnostic.aspectKey}-${diagnostic.facetId}`}>{diagnostic.code}: {diagnostic.message}</div>)}</div> : null}
          </div>
          <div className="grid gap-3 md:grid-cols-2">
            {templatesLoading ? <div className="text-sm text-muted-foreground"><RefreshCw className="mr-2 inline h-4 w-4 animate-spin" />Loading aspect templates</div> : templates.map((template) => {
              const checked = selected.includes(template.key);
              return <button type="button" key={template.key} onClick={() => setSelected((current) => checked ? current.filter((key) => key !== template.key) : [...current, template.key])} className={`rounded-lg border p-4 text-left transition-colors ${checked ? "border-primary/60 bg-primary/10" : "border-border/70 bg-card/70 hover:bg-accent/40"}`}>
                <div className="flex items-start gap-3"><Checkbox checked={checked} onCheckedChange={() => undefined} className="mt-1" /><div><div className="flex items-center gap-2 font-medium">{template.displayName}{checked ? <CheckCircle2 className="h-4 w-4 text-primary" /> : null}</div><p className="mt-1 text-xs leading-5 text-muted-foreground">{template.description}</p></div></div>
                <div className="mt-3 space-y-2">{template.facets.slice(0, 3).map((facet) => <div key={facet.facetId} className="text-xs"><span className="font-medium">{facet.metadata?.label ?? facet.facetId}</span><div className="mt-1"><ConstraintBadges facet={facet} /></div></div>)}</div>
              </button>;
            })}
          </div>
        </div>
        <DialogFooter><Button variant="outline" onClick={() => setOpen(false)}>Cancel</Button><Button onClick={submit} disabled={saving || !definitionId || selected.length === 0}>{saving ? "Creating…" : "Create definition"}</Button></DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
