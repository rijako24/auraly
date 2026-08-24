"use client";

import * as React from "react";
import {
  Area,
  Bar,
  ComposedChart,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from "recharts";

import { cn } from "@/lib/utils";
import { formatCurrency } from "@/lib/utils";

export interface OverviewChartDataPoint {
  date: string;
  revenue?: number;
  reservations?: number;
  [key: string]: string | number | undefined;
}

export interface OverviewChartProps {
  data: OverviewChartDataPoint[];
  className?: string;
}

export function OverviewChart({ data, className }: OverviewChartProps) {
  if (data.length === 0) {
    return (
      <div className={cn("flex h-[300px] w-full items-center justify-center rounded-md border border-dashed border-border/70", className)}>
        <div className="max-w-sm text-center">
          <p className="text-sm font-medium text-foreground">Sin actividad en este periodo</p>
          <p className="mt-1 text-xs text-muted-foreground">
            Cambia el periodo o espera nuevas reservas/pagos para ver la grafica.
          </p>
        </div>
      </div>
    );
  }

  return (
    <div className={cn("h-[300px] w-full", className)}>
      <ResponsiveContainer width="100%" height="100%">
        <ComposedChart
          data={data}
          margin={{ top: 10, right: 10, left: 0, bottom: 0 }}
        >
          <defs>
            <linearGradient id="overviewRevenueGradient" x1="0" y1="0" x2="0" y2="1">
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
            yAxisId="revenue"
            stroke="hsl(var(--muted-foreground))"
            fontSize={12}
            tickLine={false}
            axisLine={false}
            tickFormatter={(v) => formatCurrency(v)}
          />
          <YAxis
            yAxisId="reservations"
            orientation="right"
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
                    <p className="text-sm font-medium mb-2">{item.date}</p>
                    {item.revenue != null && (
                      <p className="text-xs">
                        Ingresos: {formatCurrency(item.revenue as number)}
                      </p>
                    )}
                    {item.reservations != null && (
                      <p className="text-xs">
                        Reservas: {item.reservations}
                      </p>
                    )}
                  </div>
                );
              }
              return null;
            }}
          />
          <Area
            yAxisId="revenue"
            type="monotone"
            dataKey="revenue"
            stroke="hsl(var(--primary))"
            fill="url(#overviewRevenueGradient)"
            strokeWidth={2}
          />
          <Bar
            yAxisId="reservations"
            dataKey="reservations"
            fill="hsl(var(--chart-2))"
            radius={[2, 2, 0, 0]}
          />
        </ComposedChart>
      </ResponsiveContainer>
    </div>
  );
}
