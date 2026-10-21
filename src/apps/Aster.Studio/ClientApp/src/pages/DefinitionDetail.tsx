import { Link, useParams } from "react-router-dom";
import { Badge } from "@/components/ui/badge";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { ConstraintBadges } from "@/components/FacetBadges";
import { ErrorState, TableSkeleton } from "@/components/StateViews";
import { api } from "@/lib/api";
import { useAsync } from "@/hooks/useAsync";

export function DefinitionDetail() {
  const { definitionId = "" } = useParams();
  const definition = useAsync(() => api.getDefinition(definitionId), [definitionId]);
  if (definition.loading) return <TableSkeleton />;
  if (definition.error || !definition.data) return <ErrorState error={definition.error} onRetry={definition.reload} />;

  return (
    <div className="space-y-6">
      <Card className="facet-seam">
        <CardHeader>
          <div className="flex flex-wrap items-start justify-between gap-3">
            <div>
              <CardTitle className="text-2xl">{definition.data.displayName}</CardTitle>
              <CardDescription>Definition version {definition.data.version} · {definition.data.aspects.length} reusable aspects · {definition.data.isSingleton ? "singleton" : "collection"}</CardDescription>
            </div>
            <Badge variant="schema">{definition.data.definitionId}</Badge>
          </div>
        </CardHeader>
      </Card>
      {definition.data.aspects.map((aspect) => (
        <Card key={aspect.aspectKey}>
          <CardHeader className="border-b border-border/60">
            <div className="flex flex-wrap items-start justify-between gap-3">
              <div>
                <CardTitle>{aspect.displayName}</CardTitle>
                <CardDescription>{aspect.requiresName ? "Requires named aspect instances" : "Single aspect instance"}</CardDescription>
              </div>
              {aspect.sharedAspectId ? <Link to={`/aspects/${encodeURIComponent(aspect.sharedAspectId)}`}><Badge variant="facet">shared {aspect.sharedAspectId} · v{aspect.sharedVersion}</Badge></Link> : <Badge variant="secondary">local</Badge>}
            </div>
          </CardHeader>
          <CardContent className="pt-0">
            <Table>
              <TableHeader><TableRow><TableHead>Facet</TableHead><TableHead>Type</TableHead><TableHead>Metadata</TableHead><TableHead>Constraints</TableHead></TableRow></TableHeader>
              <TableBody>
                {aspect.facets.map((facet) => (
                  <TableRow key={facet.facetId}>
                    <TableCell><div className="font-medium">{facet.metadata?.label ?? facet.facetId}</div><div className="text-xs text-muted-foreground">{facet.facetId}</div></TableCell>
                    <TableCell><Badge variant="schema">{facet.type}</Badge></TableCell>
                    <TableCell className="max-w-xs text-sm text-muted-foreground">{facet.metadata?.description ?? facet.metadata?.uiHint ?? "—"}</TableCell>
                    <TableCell><ConstraintBadges facet={facet} /></TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </CardContent>
        </Card>
      ))}
    </div>
  );
}
