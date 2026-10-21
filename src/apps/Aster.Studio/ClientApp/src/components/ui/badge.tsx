import * as React from "react";
import { cva, type VariantProps } from "class-variance-authority";
import { cn } from "@/lib/utils";

const badgeVariants = cva("inline-flex items-center rounded-full border px-2.5 py-0.5 text-xs font-medium transition-colors", {
  variants: {
    variant: {
      default: "border-transparent bg-primary/15 text-primary",
      secondary: "border-border/70 bg-secondary text-secondary-foreground",
      destructive: "border-transparent bg-destructive/15 text-destructive",
      outline: "border-border/70 text-foreground",
      live: "border-emerald-400/30 bg-emerald-400/10 text-emerald-300",
      preview: "border-amber-400/30 bg-amber-400/10 text-amber-300",
      facet: "border-violet-300/25 bg-violet-400/10 text-violet-200",
      schema: "border-sky-300/25 bg-sky-400/10 text-sky-200",
    },
  },
  defaultVariants: { variant: "default" },
});

export interface BadgeProps extends React.HTMLAttributes<HTMLDivElement>, VariantProps<typeof badgeVariants> {}

function Badge({ className, variant, ...props }: BadgeProps) {
  return <div className={cn(badgeVariants({ variant }), className)} {...props} />;
}

export { Badge, badgeVariants };
