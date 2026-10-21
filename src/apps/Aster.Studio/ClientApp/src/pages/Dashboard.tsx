import { ArrowRight, Boxes, BookTemplate, Clock3, Layers3, Sparkles } from "lucide-react";
import { Link } from "react-router-dom";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { ChannelBadge } from "@/components/ChannelBadge";
import { EmptyState, ErrorState, LoadingGrid } from "@/components/StateViews";
import { api, type ResourceSummaryDto } from "@/lib/api";
import { formatDate } from "@/lib/utils";
import { useAsync } from "@/hooks/useAsync";

export function Dashboard() {
  const state = useAsync(async () => {
    const definitions = await api.getDefinitions();
    const resourceLists = await Promise.all(definitions.map((definition) => api.getResources(definition.definitionId, 0, 5).catch(() => ({ items: [], totalCount: definition.resourceCount }))));
    const recent = resourceLists.flatMap((list) => list.items).sort((a, b) => Date.parse(b.created) - Date.parse(a.created)).slice(0, 7);
    const inventory = definitions.map((definition, index) => ({ definition, totalCount: resourceLists[index]?.totalCount ?? definition.resourceCount }));
    return { definitions, recent, inventory, totalItems: inventory.reduce((sum, item) => sum + item.totalCount, 0) };
  }, []);

  if (state.loading) return <LoadingGrid />;
  if (state.error || !state.data) return <ErrorState error={state.error} onRetry={state.reload} />;

  return (
    <div className="space-y-6">
      <section className="overflow-hidden rounded-xl border border-border/70 bg-card/70 shadow-seam">
        <div className="grid gap-6 p-6 facet-seam lg:grid-cols-[1fr_360px] lg:p-8">
          <div className="max-w-3xl">
            <Badge variant="facet" className="mb-4">Aster SDK showcase</Badge>
            <h2 className="text-4xl font-semibold tracking-[-0.06em] text-balance">Model content types from horizontal aspects, then let the UI compose itself.</h2>
            <p className="mt-4 text-sm leading-6 text-muted-foreground">Aster Studio demonstrates the SDK pitch: reusable aspect templates such as Content, SEO, Publishable, Taggable, and Navigation plug into any definition and instantly produce consistent forms, metadata validation, version history, and channel activation.</p>
            <div className="mt-6 flex flex-wrap gap-3">
              <Button asChild><Link to="/content-types">Create a content type <ArrowRight className="h-4 w-4" /></Link></Button>
              <Button variant="outline" asChild><Link to="/aspects">Explore aspect library</Link></Button>
            </div>
          </div>
          <div className="rounded-lg border border-border/70 bg-background/45 p-4">
            <div className="mb-4 flex items-center gap-2 text-sm font-medium"><Sparkles className="h-4 w-4 text-primary" /> Reuse signature</div>
            <div className="space-y-3">
              {["Content", "SEO", "Publishable"].map((aspect, index) => <div key={aspect} className="flex items-center gap-3 rounded-md border border-border/60 bg-card/80 p-3"><span className="flex h-7 w-7 items-center justify-center rounded-md bg-primary/10 text-xs font-semibold text-primary">{index + 1}</span><span className="font-medium">{aspect}</span><span className="ml-auto text-xs text-muted-foreground">plug-in facet group</span></div>)}
            </div>
          </div>
        </div>
      </section>
      <div className="grid gap-4 md:grid-cols-3">
        <MetricCard icon={BookTemplate} label="Content types" value={state.data.definitions.length} detail="Definitions loaded from /api/definitions" />
        <MetricCard icon={Boxes} label="Total items" value={state.data.totalItems} detail="Summed resourceCount across definitions" />
        <MetricCard icon={Layers3} label="Attached aspects" value={state.data.definitions.reduce((sum, definition) => sum + definition.aspectCount, 0)} detail="Reusable field groups powering forms" />
      </div>
      <Card>
        <CardHeader><CardTitle>Content inventory</CardTitle><CardDescription>Per-type totals are read from /api/resources totalCount.</CardDescription></CardHeader>
        <CardContent className="grid gap-3 md:grid-cols-2 lg:grid-cols-3">
          {state.data.inventory.map(({ definition, totalCount }) => <Link key={definition.definitionId} to={`/content/${definition.definitionId}`} className="rounded-lg border border-border/70 bg-background/35 p-4 transition-colors hover:bg-accent/35"><div className="flex items-center justify-between"><span className="font-medium">{definition.displayName}</span><Badge variant="schema">{totalCount}</Badge></div><p className="mt-2 text-xs text-muted-foreground">{definition.aspectCount} aspects · v{definition.version}</p></Link>)}
        </CardContent>
      </Card>
      <Card>
        <CardHeader><CardTitle className="flex items-center gap-2"><Clock3 className="h-5 w-5 text-primary" />Recent content</CardTitle><CardDescription>Latest resources from every loaded content type.</CardDescription></CardHeader>
        <CardContent>
          {state.data.recent.length === 0 ? <EmptyState title="No resources yet" description="Create an article or page to see immutable versions and channel state here." /> : <RecentTable items={state.data.recent} />}
        </CardContent>
      </Card>
    </div>
  );
}

function MetricCard({ icon: Icon, label, value, detail }: { icon: React.ComponentType<{ className?: string }>; label: string; value: number; detail: string }) {
  return <Card><CardContent className="p-5"><div className="flex items-start justify-between"><div><p className="text-sm text-muted-foreground">{label}</p><div className="mt-2 font-mono text-4xl font-semibold tracking-[-0.06em]">{value}</div></div><div className="rounded-lg border border-border/70 bg-secondary p-2 text-primary"><Icon className="h-5 w-5" /></div></div><p className="mt-4 text-xs leading-5 text-muted-foreground">{detail}</p></CardContent></Card>;
}

function RecentTable({ items }: { items: ResourceSummaryDto[] }) {
  return <Table><TableHeader><TableRow><TableHead>Title</TableHead><TableHead>Type</TableHead><TableHead>Version</TableHead><TableHead>Channels</TableHead><TableHead>Created</TableHead></TableRow></TableHeader><TableBody>{items.map((item) => <TableRow key={item.resourceId}><TableCell><Link className="font-medium hover:text-primary" to={`/content/${item.definitionId}/${item.resourceId}`}>{item.title}</Link><div className="text-xs text-muted-foreground">{item.resourceId}</div></TableCell><TableCell><Badge variant="schema">{item.definitionId}</Badge></TableCell><TableCell className="font-mono">v{item.version}</TableCell><TableCell><div className="flex gap-1.5">{item.activeChannels.length ? item.activeChannels.map((channel) => <ChannelBadge key={channel} channel={channel} />) : <span className="text-muted-foreground">Draft</span>}</div></TableCell><TableCell>{formatDate(item.created)}</TableCell></TableRow>)}</TableBody></Table>;
}
