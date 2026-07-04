import { AlertCircle, Archive, RefreshCw } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { Skeleton } from "@/components/ui/skeleton";

export function LoadingGrid() {
  return <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-3">{Array.from({ length: 6 }).map((_, index) => <Skeleton key={index} className="h-36 rounded-lg" />)}</div>;
}

export function TableSkeleton() {
  return <div className="space-y-3">{Array.from({ length: 6 }).map((_, index) => <Skeleton key={index} className="h-14 w-full rounded-md" />)}</div>;
}

export function EmptyState({ title, description, action }: { title: string; description: string; action?: React.ReactNode }) {
  return (
    <Card className="border-dashed bg-card/55">
      <CardContent className="flex flex-col items-center justify-center gap-3 py-12 text-center">
        <div className="rounded-full border border-border/70 bg-secondary p-3 text-muted-foreground"><Archive className="h-5 w-5" /></div>
        <div><h3 className="font-semibold tracking-tight">{title}</h3><p className="mt-1 max-w-md text-sm leading-6 text-muted-foreground">{description}</p></div>
        {action}
      </CardContent>
    </Card>
  );
}

export function ErrorState({ title = "The studio could not load this view", error, onRetry }: { title?: string; error: Error | null; onRetry?: () => void }) {
  return (
    <Card className="border-destructive/30 bg-destructive/5">
      <CardContent className="flex items-start gap-4 p-5">
        <div className="rounded-full bg-destructive/15 p-2 text-destructive"><AlertCircle className="h-5 w-5" /></div>
        <div className="min-w-0 flex-1"><h3 className="font-semibold tracking-tight">{title}</h3><p className="mt-1 text-sm text-muted-foreground">{error?.message ?? "Unknown error"}</p></div>
        {onRetry ? <Button variant="outline" size="sm" onClick={onRetry}><RefreshCw className="h-4 w-4" />Retry</Button> : null}
      </CardContent>
    </Card>
  );
}
