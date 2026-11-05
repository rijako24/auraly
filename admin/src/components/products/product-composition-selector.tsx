"use client";

import {Tabs,TabsList,TabsTrigger} from "@/components/ui/tabs";

export function ProductCompositionSelector({value,onChange,options,disabled=false}:{
  value:string;onChange:(value:string)=>void;options:ReadonlyArray<{code:string;label:string}>;disabled?:boolean;
}){
  return <Tabs value={value} onValueChange={onChange} aria-label="Composición del producto">
    <TabsList className="relative grid h-11 w-full grid-cols-2 rounded-xl p-1">
      <span aria-hidden className={`pointer-events-none absolute inset-y-1 left-1 w-[calc(50%-0.25rem)] rounded-lg bg-background shadow-sm ring-1 ring-border transition-transform duration-200 motion-reduce:transition-none ${value==="Combo"?"translate-x-full":"translate-x-0"}`}/>
      {options.map(option=><TabsTrigger key={option.code} value={option.code} disabled={disabled}
        className="relative z-10 h-full rounded-lg data-[state=active]:bg-transparent data-[state=active]:text-primary data-[state=active]:shadow-none">{option.label}</TabsTrigger>)}
    </TabsList>
  </Tabs>;
}
