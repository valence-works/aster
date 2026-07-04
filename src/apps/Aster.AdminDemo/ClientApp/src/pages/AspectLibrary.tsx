import { Layers3, Sparkles } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { ConstraintBadges } from "@/components/FacetBadges";
import { ErrorState, LoadingGrid } from "@/components/StateViews";
import { api } from "@/lib/api";
import { useAsync } from "@/hooks/useAsync";

export function AspectLibrary() {
  const templates = useAsync(api.getAspectTemplates, []);
  if (templates.loading) return <LoadingGrid />;
  if (templates.error) return <ErrorState error={templates.error} onRetry={templates.reload} />;

  return (
    <div className="space-y-6">
      <section className="rounded-xl border border-border/70 bg-card/70 p-6 facet-seam">
        <div className="flex items-start gap-4">
          <div className="rounded-xl border border-violet-300/25 bg-violet-400/10 p-3 text-violet-200"><Sparkles className="h-6 w-6" /></div>
          <div className="max-w-3xl">
            <p className="text-sm font-medium uppercase tracking-[0.18em] text-primary">Horizontal aspects</p>
            <h2 className="mt-2 text-3xl font-semibold tracking-[-0.05em] text-balance">Define once, attach anywhere.</h2>
            <p className="mt-3 text-sm leading-6 text-muted-foreground">Each aspect is a reusable field group. Plug SEO into Article or Page and the same facets, constraints, generated form controls, and server validation follow instantly.</p>
          </div>
        </div>
      </section>
      <div className="grid gap-4 lg:grid-cols-2">
        {templates.data?.map((template) => (
          <Card key={template.key} className="overflow-hidden">
            <CardHeader className="border-b border-border/60 bg-secondary/20">
              <div className="flex items-start justify-between gap-3">
                <div>
                  <CardTitle className="flex items-center gap-2"><Layers3 className="h-5 w-5 text-studio-facet" />{template.displayName}</CardTitle>
                  <CardDescription>{template.description}</CardDescription>
                </div>
                <Badge variant="facet">{template.key}</Badge>
              </div>
            </CardHeader>
            <CardContent className="pt-6">
              <div className="space-y-4">
                {template.facets.map((facet) => (
                  <div key={facet.facetId} className="rounded-md border border-border/60 bg-background/35 p-4">
                    <div className="flex flex-wrap items-center gap-2">
                      <span className="font-medium">{facet.metadata?.label ?? facet.facetId}</span>
                      <Badge variant="schema">{facet.type}</Badge>
                    </div>
                    {facet.metadata?.description ? <p className="mt-2 text-sm text-muted-foreground">{facet.metadata.description}</p> : null}
                    <div className="mt-3"><ConstraintBadges facet={facet} /></div>
                  </div>
                ))}
              </div>
            </CardContent>
          </Card>
        ))}
      </div>
    </div>
  );
}
