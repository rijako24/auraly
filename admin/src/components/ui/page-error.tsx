"use client";

import Image from "next/image";
import { Cloud, RefreshCw, Sparkles } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";

interface PageErrorProps {
  message?: string;
  onRetry?: () => void;
}

export function PageError({
  message = "Tuvimos un pequeño tropiezo al traer esta información. Tu trabajo sigue seguro y podemos intentarlo otra vez.",
  onRetry,
}: PageErrorProps) {
  return (
    <Card className="mx-auto mt-12 max-w-xl overflow-hidden rounded-[2rem] border-teal-100 bg-gradient-to-br from-white via-cyan-50/60 to-teal-50 shadow-xl shadow-teal-950/5">
      <CardContent className="flex flex-col items-center px-7 py-10 text-center sm:px-12">
        <div className="relative h-36 w-52" aria-hidden="true">
          <div className="absolute inset-x-8 bottom-2 h-20 rounded-[50%] bg-teal-200/30 blur-2xl" />
          <Cloud className="absolute left-1 top-10 h-12 w-12 fill-cyan-100 text-cyan-300" />
          <Cloud className="absolute right-0 top-3 h-16 w-16 fill-teal-100 text-teal-300" />
          <Sparkles className="absolute left-9 top-1 h-6 w-6 text-amber-400" />
          <Sparkles className="absolute bottom-7 right-8 h-5 w-5 text-cyan-500" />
          <span className="absolute left-1/2 top-1/2 grid h-24 w-24 -translate-x-1/2 -translate-y-1/2 place-items-center overflow-hidden rounded-[1.75rem] bg-white shadow-xl ring-1 ring-teal-900/10">
            <Image src="/brand/auraly-app-icon-192-v4.png" width={96} height={96} alt="" className="h-full w-full object-cover" />
          </span>
        </div>
        <p className="mt-2 text-xs font-bold uppercase tracking-[.2em] text-teal-700">Auraly sigue contigo</p>
        <div className="mt-2" role="alert" aria-live="polite">
          <h3 className="text-2xl font-black tracking-tight text-slate-950">
            {onRetry ? "Algo no salió como esperábamos" : "Un paso antes de continuar"}
          </h3>
          <p className="mx-auto mt-3 max-w-md text-sm leading-6 text-slate-600">{message}</p>
        </div>
        {onRetry && (
          <Button className="mt-6 rounded-xl bg-teal-700 px-6 text-white shadow-md shadow-teal-900/10 hover:bg-teal-800" onClick={onRetry}>
            <RefreshCw className="mr-2 h-4 w-4" />
            Intentar de nuevo
          </Button>
        )}
      </CardContent>
    </Card>
  );
}
