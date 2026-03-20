"use client";

import { BaseEdge, type EdgeProps, Position } from "@xyflow/react";
import { memo, useMemo } from "react";

const DETOUR = 56;

/**
 * Arista de retorno (ciclo): sale del nodo, baja (o se desvía lateralmente en layout vertical)
 * y vuelve al destino sin cruzar el “camino principal” como una bezier directa.
 */
function FlowBackEdgeInner(props: EdgeProps) {
  const {
    id,
    sourceX,
    sourceY,
    targetX,
    targetY,
    sourcePosition,
    targetPosition,
    style,
    markerEnd,
    label,
    labelStyle,
    labelShowBg,
    labelBgStyle,
    labelBgPadding,
    labelBgBorderRadius,
    interactionWidth,
    selected,
  } = props;

  const { path, labelX, labelY } = useMemo(() => {
    const useVerticalDetour =
      sourcePosition === Position.Right ||
      sourcePosition === Position.Left ||
      sourcePosition === undefined;

    if (useVerticalDetour) {
      const lowY = Math.max(sourceY, targetY) + DETOUR;
      const pathStr = `M ${sourceX} ${sourceY} L ${sourceX} ${lowY} L ${targetX} ${lowY} L ${targetX} ${targetY}`;
      const lx = (sourceX + targetX) / 2;
      const ly = lowY;
      return { path: pathStr, labelX: lx, labelY: ly };
    }

    const sideX = Math.max(sourceX, targetX) + DETOUR;
    const pathStr = `M ${sourceX} ${sourceY} L ${sideX} ${sourceY} L ${sideX} ${targetY} L ${targetX} ${targetY}`;
    const lx = sideX;
    const ly = (sourceY + targetY) / 2;
    return { path: pathStr, labelX: lx, labelY: ly };
  }, [sourceX, sourceY, targetX, targetY, sourcePosition]);

  return (
    <>
      <BaseEdge
        id={id}
        path={path}
        style={{
          ...style,
          strokeDasharray: style?.strokeDasharray ?? "8 5",
          strokeWidth: (style?.strokeWidth as number) ?? 1.75,
          opacity: selected === true ? 1 : 0.92,
        }}
        markerEnd={markerEnd}
        interactionWidth={interactionWidth ?? 22}
        labelX={labelX}
        labelY={labelY}
        label={label}
        labelStyle={{
          ...labelStyle,
          fontSize: 10,
        }}
        labelShowBg={labelShowBg ?? true}
        labelBgStyle={labelBgStyle}
        labelBgPadding={labelBgPadding}
        labelBgBorderRadius={labelBgBorderRadius}
      />
    </>
  );
}

export const FlowBackEdge = memo(FlowBackEdgeInner);
