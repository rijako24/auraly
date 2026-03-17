"use client";

import * as React from "react";

import { cn } from "@/lib/utils";
import { Card, CardContent, CardFooter, CardHeader } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";

export interface EntityCardField {
  label: string;
  value: React.ReactNode;
  highlight?: boolean;
}

export interface EntityCardProps {
  title: string;
  subtitle?: string;
  fields?: EntityCardField[];
  badges?: { label: string; variant?: "default" | "secondary" | "destructive" | "outline" }[];
  actions?: { label: string; onClick: () => void; variant?: "default" | "outline" | "ghost" }[];
  children?: React.ReactNode;
  className?: string;
}

export function EntityCard({
  title,
  subtitle,
  fields = [],
  badges = [],
  actions = [],
  children,
  className,
}: EntityCardProps) {
  return (
    <Card className={cn("flex flex-col", className)}>
      <CardHeader className="pb-2">
        <div className="flex items-start justify-between gap-2">
          <div className="space-y-1">
            <h3 className="font-semibold leading-tight">{title}</h3>
            {subtitle && (
              <p className="text-sm text-muted-foreground">{subtitle}</p>
            )}
          </div>
          {badges.length > 0 && (
            <div className="flex flex-wrap gap-1">
              {badges.map((badge) => (
                <Badge key={badge.label} variant={badge.variant ?? "secondary"}>
                  {badge.label}
                </Badge>
              ))}
            </div>
          )}
        </div>
      </CardHeader>
      {(fields.length > 0 || children) && (
        <CardContent className="flex-1 space-y-2 pt-0">
          {fields.map((field) => (
            <div key={field.label} className="flex justify-between gap-4 text-sm">
              <span
                className={cn(
                  "text-muted-foreground",
                  field.highlight && "font-medium text-foreground"
                )}
              >
                {field.label}:
              </span>
              <span
                className={cn(
                  "text-right",
                  field.highlight && "font-medium"
                )}
              >
                {field.value}
              </span>
            </div>
          ))}
          {children}
        </CardContent>
      )}
      {actions.length > 0 && (
        <CardFooter className="flex flex-wrap gap-2 pt-2">
          {actions.map((action) => (
            <Button
              key={action.label}
              variant={action.variant ?? "outline"}
              size="sm"
              onClick={action.onClick}
            >
              {action.label}
            </Button>
          ))}
        </CardFooter>
      )}
    </Card>
  );
}
