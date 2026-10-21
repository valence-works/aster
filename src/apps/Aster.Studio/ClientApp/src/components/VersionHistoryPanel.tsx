import { useState } from "react";
import { Eye, History, RadioTower } from "lucide-react";
import { toast } from "sonner";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { ChannelBadge } from "@/components/ChannelBadge";
import { DynamicResourceForm } from "@/components/DynamicResourceForm";
import { ErrorState, TableSkeleton } from "@/components/StateViews";
import { api, type Channel, type DefinitionDetailDto, type ResourceDetailDto, type VersionSummaryDto } from "@/lib/api";
import { formatDate } from "@/lib/utils";
import { useAsync } from "@/hooks/useAsync";

type PendingAction = { version: number; isLatest: boolean; channel: Channel; action: "activate" | "deactivate" } | null;

export function VersionHistoryPanel({ resourceId, latestVersion, definition, onChanged }: { resourceId: string; latestVersion: number; definition: DefinitionDetailDto; onChanged: () => void }) {
  // Reloads when the editor saves a new version, not only when it opens another resource.
  const versions = useAsync(() => api.getResourceVersions(resourceId), [resourceId, latestVersion]);
  const [viewing, setViewing] = useState<ResourceDetailDto | null>(null);
  const [pending, setPending] = useState<PendingAction>(null);
  const [busy, setBusy] = useState(false);

  async function viewVersion(version: number) {
    try {
      setViewing(await api.getResourceVersion(resourceId, version));
    } catch (error) {
      toast.error("Could not load version", { description: error instanceof Error ? error.message : String(error) });
    }
  }

  async function runPending() {
    if (!pending) return;
    setBusy(true);
    try {
      if (pending.action === "activate") {
        await api.activateResource(resourceId, pending.version, pending.channel);
        toast.success(`v${pending.version} is now ${pending.channel}`, { description: "Channel activation updated." });
      } else {
        await api.deactivateResource(resourceId, pending.version, pending.channel);
        toast.success(`v${pending.version} left ${pending.channel}`, { description: "Channel activation removed." });
      }
      versions.reload();
      onChanged();
      setPending(null);
    } catch (error) {
      toast.error("Channel update failed", { description: error instanceof Error ? error.message : String(error) });
    } finally {
      setBusy(false);
    }
  }

  if (versions.loading) return <TableSkeleton />;
  if (versions.error) return <ErrorState error={versions.error} onRetry={versions.reload} />;

  return (
    <Card>
      <CardHeader><CardTitle className="flex items-center gap-2"><History className="h-5 w-5 text-primary" />Version history</CardTitle><CardDescription>Immutable versions can be activated independently to preview and live. Maintenance hints come from IResourceVersionHistoryService: latest and active versions are protected from pruning.</CardDescription></CardHeader>
      <CardContent>
        <Tabs defaultValue="timeline">
          <TabsList><TabsTrigger value="timeline">Timeline</TabsTrigger><TabsTrigger value="channels">Channels</TabsTrigger></TabsList>
          <TabsContent value="timeline" className="space-y-3">
            {(versions.data ?? []).map((version) => <VersionRow key={version.version} version={version} onView={() => viewVersion(version.version)} onAction={(channel, action) => setPending({ version: version.version, isLatest: version.isLatest, channel, action })} />)}
          </TabsContent>
          <TabsContent value="channels" className="grid gap-3 md:grid-cols-2">
            {(["preview", "live"] as Channel[]).map((channel) => <div key={channel} className="rounded-lg border border-border/70 bg-background/35 p-4"><ChannelBadge channel={channel} /><div className="mt-3 space-y-2">{(versions.data ?? []).filter((version) => version.activeChannels.includes(channel)).map((version) => <div key={version.version} className="flex items-center justify-between rounded-md bg-secondary/45 px-3 py-2"><span className="font-mono">v{version.version}</span><span className="text-xs text-muted-foreground">{formatDate(version.created)}</span></div>)}{!(versions.data ?? []).some((version) => version.activeChannels.includes(channel)) ? <p className="text-sm text-muted-foreground">No version active on {channel}.</p> : null}</div></div>)}
          </TabsContent>
        </Tabs>
      </CardContent>
      <Dialog open={!!viewing} onOpenChange={(open) => !open && setViewing(null)}>
        <DialogContent className="max-w-5xl"><DialogHeader><DialogTitle>Viewing v{viewing?.version}</DialogTitle><DialogDescription>Read-only render through the same generated form.</DialogDescription></DialogHeader>{viewing ? <DynamicResourceForm definition={definition} aspects={viewing.aspects} errors={{}} readOnly /> : null}</DialogContent>
      </Dialog>
      <Dialog open={!!pending} onOpenChange={(open) => !open && setPending(null)}>
        <DialogContent><DialogHeader><DialogTitle>{pending?.action === "activate" ? "Activate" : "Deactivate"} v{pending?.version} {pending?.channel}</DialogTitle><DialogDescription>This updates channel state without rewriting the immutable resource version.{pending && !pending.isLatest && pending.action === "activate" ? ` Historical activation: v${latestVersion} stays the latest version.` : ""}</DialogDescription></DialogHeader><DialogFooter><Button variant="outline" onClick={() => setPending(null)}>Cancel</Button><Button onClick={runPending} disabled={busy}><RadioTower className="h-4 w-4" />{busy ? "Updating…" : "Confirm"}</Button></DialogFooter></DialogContent>
      </Dialog>
    </Card>
  );
}

function VersionRow({ version, onView, onAction }: { version: VersionSummaryDto; onView: () => void; onAction: (channel: Channel, action: "activate" | "deactivate") => void }) {
  return (
    <div className="rounded-lg border border-border/70 bg-background/35 p-4">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div><div className="flex flex-wrap items-center gap-2"><span className="font-mono text-lg font-semibold">v{version.version}</span>{version.isLatest ? <Badge variant="schema">latest</Badge> : null}{version.maintenanceDisposition === "protected" ? <Badge variant="outline">protected</Badge> : <Badge variant="preview">possible prune candidate</Badge>}</div><p className="mt-1 text-sm text-muted-foreground">{formatDate(version.created)} · definition v{version.definitionVersion ?? "?"}</p></div>
        <div className="flex flex-wrap gap-2"><Button size="sm" variant="outline" onClick={onView}><Eye className="h-4 w-4" />View</Button>{(["preview", "live"] as Channel[]).map((channel) => <Button key={channel} size="sm" variant={version.activeChannels.includes(channel) ? "secondary" : "outline"} onClick={() => onAction(channel, version.activeChannels.includes(channel) ? "deactivate" : "activate")}>{version.activeChannels.includes(channel) ? "Deactivate" : "Activate"} {channel}</Button>)}</div>
      </div>
      <div className="mt-3 flex flex-wrap gap-1.5">{version.activeChannels.length ? version.activeChannels.map((channel) => <ChannelBadge key={channel} channel={channel} />) : <span className="text-sm text-muted-foreground">No active channels</span>}</div>
    </div>
  );
}
