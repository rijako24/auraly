"use client";

import * as React from "react";
import {
  Bar,
  BarChart,
  Cell,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from "recharts";

import { cn } from "@/lib/utils";

export interface ReservationsChartDataPoint {
  name: string;
  confirmed?: number;
  pending?: number;
  cancelled?: number;
  completed?: number;
  [key: string]: string | number | undefined;
}

export interface ReservationsChartProps {
  data: ReservationsChartDataPoint[];
  className?: string;
}

const STATUS_COLORS: Record<string, string> = {
  confirmed: "hsl(var(--primary))",
  pending: "hsl(var(--chart-2))",
  cancelled: "hsl(var(--destructive))",
  completed: "hsl(var(--chart-1))",
};

export function ReservationsChart({
  data,
  className,
}: ReservationsChartProps) {
  const keys = React.useMemo(() => {
    const allKeys = new Set<string>();
    data.forEach((d) => {
      Object.keys(d).forEach((k) => {
        if (k !== "name" && typeof d[k] === "number") allKeys.add(k);
      });
    });
    return Array.from(allKeys);
  }, [data]);

  const colorMap = keys.reduce(
    (acc, k, i) => {
      acc[k] =
        STATUS_COLORS[k] ??
        `hsl(var(--chart-${((i % 5) + 1) as 1 | 2 | 3 | 4 | 5}))`;
      return acc;
    },
    {} as Record<string, string>
  );

  return (
    <div className={cn("h-[300px] w-full", className)}>
      <ResponsiveContainer width="100%" height="100%">
        <BarChart
          data={data}
          margin={{ top: 10, right: 10, left: 0, bottom: 0 }}
          barGap={4}
        >
          <XAxis
            dataKey="name"
            stroke="hsl(var(--muted-foreground))"
            fontSize={12}
            tickLine={false}
            axisLine={false}
          />
          <YAxis
            stroke="hsl(var(--muted-foreground))"
            fontSize={12}
            tickLine={false}
            axisLine={false}
          />
          <Tooltip
            content={({ active, payload }) => {
              if (active && payload?.length) {
                const item = payload[0].payload;
                return (
                  <div className="rounded-lg border bg-background px-3 py-2 shadow-sm">
                    <p className="text-sm font-medium mb-1">{item.name}</p>
                    {Object.entries(item)
                      .filter(([k, v]) => k !== "name" && typeof v === "number")
                      .map(([k, v]) => (
                        <div
                          key={k}
                          className="flex justify-between gap-4 text-xs"
                        >
                          <span className="text-muted-foreground capitalize">
                            {k}:
                          </span>
                          <span>{String(v)}</span>
                        </div>
                      ))}
                  </div>
                );
              }
              return null;
            }}
          />
          {keys.map((key) => (
            <Bar
              key={key}
              dataKey={key}
              fill={colorMap[key]}
              radius={[2, 2, 0, 0]}
            />
          ))}
        </BarChart>
      </ResponsiveContainer>
    </div>
  );
}
