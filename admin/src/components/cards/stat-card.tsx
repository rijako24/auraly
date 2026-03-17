"use client";

import * as React from "react";
import { ArrowDown, ArrowUp, type LucideIcon } from "lucide-react";

import { cn } from "@/lib/utils";
import { Card, CardContent, CardHeader } from "@/components/ui/card";

export interface StatCardProps {
  title: string;
  value: string | number;
  change?: number;
  changeLabel?: string;
  icon: LucideIcon;
  iconClassName?: string;
  trend?: "up" | "down" | "neutral";
  className?: string;
}

export function StatCard({
  title,
  value,
  change,
  changeLabel,
  icon: Icon,
  iconClassName,
  trend,
  className,
}: StatCardProps) {
  const computedTrend =
    trend ??
    (change != null
      ? change > 0
        ? "up"
        : change < 0
          ? "down"
          : "neutral"
      : "neutral");

  const trendColors = {
    up: "text-emerald-600 dark:text-emerald-400",
    down: "text-red-600 dark:text-red-400",
    neutral: "text-muted-foreground",
  };

  return (
    <Card
      className={cn(
        "relative overflow-hidden transition-shadow hover:shadow-md",
        className
      )}
    >
      <div className="absolute inset-0 bg-gradient-to-br from-primary/5 via-transparent to-primary/10 pointer-events-none" />
      <CardHeader className="flex flex-row items-center justify-between pb-2">
        <p className="text-sm font-medium text-muted-foreground">{title}</p>
        <Icon
          className={cn(
            "h-4 w-4 text-muted-foreground",
            iconClassName
          )}
        />
      </CardHeader>
      <CardContent>
        <div className="text-2xl font-bold">{value}</div>
        {(change != null || changeLabel) && (
          <div
            className={cn(
              "mt-1 flex items-center gap-1 text-xs font-medium",
              trendColors[computedTrend]
            )}
          >
            {change != null && (
              <>
                {computedTrend === "up" && (
                  <ArrowUp className="h-3 w-3" />
                )}
                {computedTrend === "down" && (
                  <ArrowDown className="h-3 w-3" />
                )}
                <span>{change > 0 ? "+" : ""}{change}%</span>
              </>
            )}
            {changeLabel && <span className="text-muted-foreground">{changeLabel}</span>}
          </div>
        )}
      </CardContent>
    </Card>
  );
}
