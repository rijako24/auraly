export function printPosHtmlDocument(html: string, failureMessage: string): Promise<void> {
  return new Promise((resolve, reject) => {
    const frame = document.createElement("iframe");
    frame.setAttribute("aria-hidden", "true");
    frame.style.position = "fixed";
    frame.style.width = "1px";
    frame.style.height = "1px";
    frame.style.opacity = "0";
    frame.style.pointerEvents = "none";
    const remove = () => frame.remove();
    frame.onload = () => {
      window.setTimeout(() => {
        const printWindow = frame.contentWindow;
        if (!printWindow) {
          remove();
          reject(new Error(failureMessage));
          return;
        }
        printWindow.addEventListener("afterprint", remove, { once: true });
        printWindow.focus();
        // Dispatch is complete once the browser owns the print dialog. Do not
        // keep the POS operation pending until the user prints or cancels it.
        resolve();
        window.setTimeout(() => {
          try {
            printWindow.print();
          } finally {
            window.setTimeout(remove, 60_000);
          }
        }, 0);
      }, 150);
    };
    frame.srcdoc = html;
    document.body.appendChild(frame);
  });
}
