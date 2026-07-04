import { Badge } from "@/components/ui/badge";

export function ChannelBadge({ channel }: { channel: string }) {
  if (channel === "live") return <Badge variant="live">live</Badge>;
  if (channel === "preview") return <Badge variant="preview">preview</Badge>;
  return <Badge variant="outline">{channel}</Badge>;
}
