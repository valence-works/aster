import { ChevronLeft, ChevronRight, Plus, ShieldCheck } from "lucide-react";
import { Link, useNavigate, useParams } from "react-router-dom";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { ChannelBadge } from "@/components/ChannelBadge";
import { TagInput } from "@/components/TagInput";
import { LifecycleBadge } from "@/components/LifecycleBadge";
import { EmptyState, ErrorState, TableSkeleton } from "@/components/StateViews";
import { api, type LifecycleFilter } from "@/lib/api";
import { formatDate } from "@/lib/utils";
import { useAsync } from "@/hooks/useAsync";
import { useState } from "react";

const take = 20;

const lifecycleFilters: Array<{ value: LifecycleFilter; label: string }> = [
  { value: "all", label: "All lifecycle states" },
  { value: "active", label: "Active" },
  { value: "archived", label: "Archived" },
  { value: "soft-deleted", label: "Soft-deleted" },
];

export function ContentList() {
  const { definitionId = "" } = useParams();
  // Keyed by definition: the route reuses this component across content types, and a fresh instance drops the
  // previous type's tag filters and page instead of applying them to a type that may not even be taggable.
  return <DefinitionContentList key={definitionId} definitionId={definitionId} />;
}

function DefinitionContentList({ definitionId }: { definitionId: string }) {
  const [skip, setSkip] = useState(0);
  const [tags, setTags] = useState<string[]>([]);
  const [lifecycle, setLifecycle] = useState<LifecycleFilter>("all");
  const navigate = useNavigate();
  const definition = useAsync(() => api.getDefinition(definitionId), [definitionId]);
  // The server filters with ArrayContainsAny, so a resource matches when it has any of the selected tags.
  const resources = useAsync(() => api.getResources(definitionId, skip, take, tags, lifecycle), [definitionId, skip, tags.join("\u0000"), lifecycle]);
  const taggable = definition.data?.aspects.some((aspect) => aspect.aspectKey === "taggable") ?? false;

  function filterBy(next: string[]) {
    setTags(next);
    setSkip(0);
  }

  function filterByLifecycle(next: LifecycleFilter) {
    setLifecycle(next);
    setSkip(0);
  }

  if (definition.loading || resources.loading) return <TableSkeleton />;
  if (definition.error) return <ErrorState error={definition.error} onRetry={definition.reload} />;
  if (resources.error) return <ErrorState error={resources.error} onRetry={resources.reload} />;

  const total = resources.data?.totalCount ?? 0;
  const items = resources.data?.items ?? [];
  const activeFilters = [tags.length ? `tagged ${tags.join(" or ")}` : null, lifecycle !== "all" ? lifecycle : null].filter(Boolean);

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div><h2 className="text-2xl font-semibold tracking-[-0.05em]">{definition.data?.displayName ?? definitionId}</h2><p className="mt-1 text-sm text-muted-foreground">Definition v{definition.data?.version} · generated from {definition.data?.aspects.length ?? 0} aspects.</p></div>
        <div className="flex gap-2"><Button variant="outline" asChild><Link to={`/content-types/${definitionId}`}><ShieldCheck className="h-4 w-4" />Policies</Link></Button><Button asChild><Link to={`/content/${definitionId}/new`}><Plus className="h-4 w-4" />New {definition.data?.displayName ?? "Resource"}</Link></Button></div>
      </div>
      <Card>
        <CardHeader><CardTitle>Resources</CardTitle><CardDescription>{total} total items. Active channels show whether any version is activated to preview or live.</CardDescription></CardHeader>
        <CardContent>
          <div className="mb-4 flex flex-wrap items-start gap-3">
            {taggable ? <div className="min-w-64 max-w-xl flex-1 space-y-1"><TagInput value={tags} onChange={filterBy} placeholder="Filter by tag (matches any)" /></div> : null}
            <Select value={lifecycle} onValueChange={(value) => filterByLifecycle(value as LifecycleFilter)}><SelectTrigger aria-label="Lifecycle filter" className="w-52"><SelectValue /></SelectTrigger><SelectContent>{lifecycleFilters.map((filter) => <SelectItem key={filter.value} value={filter.value}>{filter.label}</SelectItem>)}</SelectContent></Select>
          </div>
          {items.length === 0 && activeFilters.length > 0 ? <EmptyState title="No matching resources" description={`Nothing is ${activeFilters.join(" and ")}.`} action={<Button variant="outline" onClick={() => { filterBy([]); setLifecycle("all"); }}>Clear filters</Button>} /> : items.length === 0 ? <EmptyState title={`No ${definition.data?.displayName ?? definitionId} resources`} description="Create the first resource to see immutable versions and channel activation." action={<Button asChild><Link to={`/content/${definitionId}/new`}>Create resource</Link></Button>} /> : (
            <Table>
              <TableHeader><TableRow><TableHead>Title</TableHead><TableHead>Version</TableHead><TableHead>Created</TableHead><TableHead>Active channels</TableHead><TableHead>Lifecycle</TableHead>{taggable ? <TableHead>Tags</TableHead> : null}</TableRow></TableHeader>
              <TableBody>{items.map((item) => <TableRow key={item.resourceId} className="cursor-pointer" onClick={() => navigate(`/content/${definitionId}/${item.resourceId}`)}><TableCell><div className="font-medium">{item.title}</div><div className="text-xs text-muted-foreground">{item.resourceId}</div></TableCell><TableCell><Badge variant="schema">v{item.version}</Badge></TableCell><TableCell>{formatDate(item.created)}</TableCell><TableCell><div className="flex flex-wrap gap-1.5">{item.activeChannels.length ? item.activeChannels.map((channel) => <ChannelBadge key={channel} channel={channel} />) : <span className="text-muted-foreground">Draft</span>}</div></TableCell><TableCell><LifecycleBadge state={item.lifecycleState} /></TableCell>{taggable ? <TableCell><div className="flex flex-wrap gap-1.5">{item.tags.map((tag) => <button key={tag} type="button" aria-label={`Filter by ${tag}`} onClick={(event) => { event.stopPropagation(); if (!tags.some((selected) => selected.toLowerCase() === tag.toLowerCase())) filterBy([...tags, tag]); }}><Badge variant="facet">{tag}</Badge></button>)}</div></TableCell> : null}</TableRow>)}</TableBody>
            </Table>
          )}
          <div className="mt-4 flex items-center justify-between text-sm text-muted-foreground"><span>Showing {items.length ? skip + 1 : 0}–{Math.min(skip + take, total)} of {total}</span><div className="flex gap-2"><Button variant="outline" size="sm" disabled={skip === 0} onClick={() => setSkip(Math.max(0, skip - take))}><ChevronLeft className="h-4 w-4" />Previous</Button><Button variant="outline" size="sm" disabled={skip + take >= total} onClick={() => setSkip(skip + take)}>Next<ChevronRight className="h-4 w-4" /></Button></div></div>
        </CardContent>
      </Card>
    </div>
  );
}
