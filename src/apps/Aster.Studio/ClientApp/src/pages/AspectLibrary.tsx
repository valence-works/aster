import { ArrowUpCircle, Layers3, Plus, Sparkles } from "lucide-react";
import { Link } from "react-router-dom";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { ConstraintBadges } from "@/components/FacetBadges";
import { EmptyState, ErrorState, LoadingGrid } from "@/components/StateViews";
import { api, type SharedAspectDto } from "@/lib/api";
import { useAsync } from "@/hooks/useAsync";

export function AspectLibrary() {
  const aspects = useAsync(api.getAspects, []);
  if (aspects.loading) return <LoadingGrid />;
  if (aspects.error) return <ErrorState error={aspects.error} onRetry={aspects.reload} />;

  return (
    <div className="space-y-6">
      <section className="rounded-xl border border-border/70 bg-card/70 p-6 facet-seam">
        <div className="flex flex-wrap items-start justify-between gap-4">
          <div className="flex items-start gap-4">
            <div className="rounded-xl border border-violet-300/25 bg-violet-400/10 p-3 text-violet-200"><Sparkles className="h-6 w-6" /></div>
            <div className="max-w-3xl">
              <p className="text-sm font-medium uppercase tracking-[0.18em] text-primary">Shared aspects</p>
              <h2 className="mt-2 text-3xl font-semibold tracking-[-0.05em] text-balance">Define once, attach anywhere.</h2>
              <p className="mt-3 text-sm leading-6 text-muted-foreground">Each aspect is a versioned field group stored by Aster. Content types pin a version; saving an aspect adds a new version, and you choose when each content type upgrades.</p>
            </div>
          </div>
          <Button asChild><Link to="/aspects/new"><Plus className="h-4 w-4" />New aspect</Link></Button>
        </div>
      </section>
      {!aspects.data?.length ? <EmptyState title="No shared aspects" description="Create an aspect to reuse its facets across content types." /> : (
        <div className="grid gap-4 lg:grid-cols-2">
          {aspects.data.map((aspect) => <AspectCard key={aspect.aspectId} aspect={aspect} />)}
        </div>
      )}
    </div>
  );
}

function AspectCard({ aspect }: { aspect: SharedAspectDto }) {
  const outdated = aspect.usedBy.filter((usage) => usage.pinnedVersion < aspect.version);
  return (
    <Card className="overflow-hidden">
      <CardHeader className="border-b border-border/60 bg-secondary/20">
        <div className="flex items-start justify-between gap-3">
          <div>
            <CardTitle className="flex items-center gap-2">
              <Layers3 className="h-5 w-5 text-studio-facet" />
              <Link to={`/aspects/${encodeURIComponent(aspect.aspectId)}`} className="hover:text-primary">{aspect.displayName}</Link>
            </CardTitle>
            <CardDescription>
              {aspect.facets.length} {aspect.facets.length === 1 ? "facet" : "facets"} · used by {aspect.usedBy.length ? aspect.usedBy.map((usage) => usage.definitionId).join(", ") : "no content types"}
            </CardDescription>
          </div>
          <div className="flex shrink-0 flex-wrap justify-end gap-1.5">
            <Badge variant="facet">{aspect.aspectId}</Badge>
            <Badge variant="schema">v{aspect.version}</Badge>
          </div>
        </div>
        {outdated.length ? (
          <Link to={`/aspects/${encodeURIComponent(aspect.aspectId)}`} className="mt-3 flex items-center gap-2 text-xs text-amber-300 hover:underline">
            <ArrowUpCircle className="h-4 w-4" />
            {outdated.length} {outdated.length === 1 ? "content type pins" : "content types pin"} an older version
          </Link>
        ) : null}
      </CardHeader>
      <CardContent className="pt-6">
        <div className="space-y-4">
          {aspect.facets.map((facet) => (
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
  );
}
