"use client";

import * as React from "react";
import {
  Area,
  AreaChart,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from "recharts";

import { cn } from "@/lib/utils";
import { formatCurrency } from "@/lib/utils";
import type { ChartDataPoint } from "@/types/api";

export interface RevenueChartProps {
  data: ChartDataPoint[];
  period?: string;
  className?: string;
}

export function RevenueChart({ data, period = "7d", className }: RevenueChartProps) {
  const formattedData = React.useMemo(
    () =>
      data.map((d) => ({
        ...d,
        value: typeof d.value === "number" ? d.value : Number(d.value) || 0,
      })),
    [data]
  );

  return (
    <div className={cn("h-[300px] w-full", className)}>
      <ResponsiveContainer width="100%" height="100%">
        <AreaChart
          data={formattedData}
          margin={{ top: 10, right: 10, left: 0, bottom: 0 }}
        >
          <defs>
            <linearGradient id="revenueGradient" x1="0" y1="0" x2="0" y2="1">
              <stop offset="5%" stopColor="hsl(var(--primary))" stopOpacity={0.3} />
              <stop offset="95%" stopColor="hsl(var(--primary))" stopOpacity={0} />
            </linearGradient>
          </defs>
          <XAxis
            dataKey="date"
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
            tickFormatter={(value) => formatCurrency(value * 100)}
          />
          <Tooltip
            content={({ active, payload }) => {
              if (active && payload?.[0]) {
                const val = payload[0].value as number;
                return (
                  <div className="rounded-lg border bg-background px-3 py-2 shadow-sm">
                    <p className="text-sm font-medium">
                      {formatCurrency((val ?? 0) * 100)}
                    </p>
                    <p className="text-xs text-muted-foreground">
                      {payload[0].payload.date}
                    </p>
                  </div>
                );
              }
              return null;
            }}
          />
          <Area
            type="monotone"
            dataKey="value"
            stroke="hsl(var(--primary))"
            fill="url(#revenueGradient)"
            strokeWidth={2}
          />
        </AreaChart>
      </ResponsiveContainer>
    </div>
  );
}
