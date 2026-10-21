import { Badge } from "@/components/ui/badge";
import type { LifecycleState } from "@/lib/api";

export function LifecycleBadge({ state }: { state: LifecycleState }) {
  if (state === "archived") return <Badge variant="secondary">archived</Badge>;
  if (state === "soft-deleted") return <Badge variant="destructive">soft-deleted</Badge>;
  return <Badge variant="outline">active</Badge>;
}
