import { ChevronLeft, ChevronRight, Plus } from "lucide-react";
import { Link, useNavigate, useParams } from "react-router-dom";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { ChannelBadge } from "@/components/ChannelBadge";
import { EmptyState, ErrorState, TableSkeleton } from "@/components/StateViews";
import { api } from "@/lib/api";
import { formatDate } from "@/lib/utils";
import { useAsync } from "@/hooks/useAsync";
import { useState } from "react";

const take = 20;

export function ContentList() {
  const { definitionId = "" } = useParams();
  const [skip, setSkip] = useState(0);
  const navigate = useNavigate();
  const definition = useAsync(() => api.getDefinition(definitionId), [definitionId]);
  const resources = useAsync(() => api.getResources(definitionId, skip, take), [definitionId, skip]);

  if (definition.loading || resources.loading) return <TableSkeleton />;
  if (definition.error) return <ErrorState error={definition.error} onRetry={definition.reload} />;
  if (resources.error) return <ErrorState error={resources.error} onRetry={resources.reload} />;

  const total = resources.data?.totalCount ?? 0;
  const items = resources.data?.items ?? [];

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div><h2 className="text-2xl font-semibold tracking-[-0.05em]">{definition.data?.displayName ?? definitionId}</h2><p className="mt-1 text-sm text-muted-foreground">Definition v{definition.data?.version} · generated from {definition.data?.aspects.length ?? 0} aspects.</p></div>
        <Button asChild><Link to={`/content/${definitionId}/new`}><Plus className="h-4 w-4" />New {definition.data?.displayName ?? "Resource"}</Link></Button>
      </div>
      <Card>
        <CardHeader><CardTitle>Resources</CardTitle><CardDescription>{total} total items. Active channels show whether any version is activated to preview or live.</CardDescription></CardHeader>
        <CardContent>
          {items.length === 0 ? <EmptyState title={`No ${definition.data?.displayName ?? definitionId} resources`} description="Create the first resource to see immutable versions and channel activation." action={<Button asChild><Link to={`/content/${definitionId}/new`}>Create resource</Link></Button>} /> : (
            <Table>
              <TableHeader><TableRow><TableHead>Title</TableHead><TableHead>Version</TableHead><TableHead>Created</TableHead><TableHead>Active channels</TableHead></TableRow></TableHeader>
              <TableBody>{items.map((item) => <TableRow key={item.resourceId} className="cursor-pointer" onClick={() => navigate(`/content/${definitionId}/${item.resourceId}`)}><TableCell><div className="font-medium">{item.title}</div><div className="text-xs text-muted-foreground">{item.resourceId}</div></TableCell><TableCell><Badge variant="schema">v{item.version}</Badge></TableCell><TableCell>{formatDate(item.created)}</TableCell><TableCell><div className="flex flex-wrap gap-1.5">{item.activeChannels.length ? item.activeChannels.map((channel) => <ChannelBadge key={channel} channel={channel} />) : <span className="text-muted-foreground">Draft</span>}</div></TableCell></TableRow>)}</TableBody>
            </Table>
          )}
          <div className="mt-4 flex items-center justify-between text-sm text-muted-foreground"><span>Showing {items.length ? skip + 1 : 0}–{Math.min(skip + take, total)} of {total}</span><div className="flex gap-2"><Button variant="outline" size="sm" disabled={skip === 0} onClick={() => setSkip(Math.max(0, skip - take))}><ChevronLeft className="h-4 w-4" />Previous</Button><Button variant="outline" size="sm" disabled={skip + take >= total} onClick={() => setSkip(skip + take)}>Next<ChevronRight className="h-4 w-4" /></Button></div></div>
        </CardContent>
      </Card>
    </div>
  );
}
