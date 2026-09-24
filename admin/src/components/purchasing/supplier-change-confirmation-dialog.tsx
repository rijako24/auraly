"use client";

import { Button } from "@/components/ui/button";
import {
  Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle,
} from "@/components/ui/dialog";

type SupplierChangeConfirmationDialogProps = {
  open: boolean;
  supplierName?: string;
  productCount: number;
  documentName: "recepción" | "orden";
  onCancel: () => void;
  onConfirm: () => void;
};

export function SupplierChangeConfirmationDialog({
  open, supplierName, productCount, documentName, onCancel, onConfirm,
}: SupplierChangeConfirmationDialogProps) {
  return <Dialog open={open} onOpenChange={(value) => !value && onCancel()}>
    <DialogContent className="sm:max-w-md">
      <DialogHeader>
        <DialogTitle>{supplierName ? "Cambiar proveedor" : "Quitar proveedor"}</DialogTitle>
        <DialogDescription>
          Esta {documentName} ya tiene {productCount} {productCount === 1 ? "producto agregado" : "productos agregados"}.
          {supplierName
            ? ` Al cambiar a ${supplierName}, limpiaremos esas líneas para evitar mezclar productos, costos o códigos de proveedores distintos.`
            : " Al quitar el proveedor, limpiaremos esas líneas porque sus productos y costos dependen de él."}
        </DialogDescription>
      </DialogHeader>
      <p className="text-sm text-muted-foreground">La captura quedará abierta para continuar.</p>
      <DialogFooter>
        <Button type="button" variant="outline" onClick={onCancel}>Conservar proveedor actual</Button>
        <Button type="button" variant="destructive" onClick={onConfirm}>{supplierName ? "Cambiar" : "Quitar"} y limpiar productos</Button>
      </DialogFooter>
    </DialogContent>
  </Dialog>;
}
