import { createContext, useContext, useState } from "react";
import type { ReactNode } from "react";
import { CalculatorModal } from "./CalculatorModal";

const CalculatorContext = createContext<{ openCalculator: (tabOrEvent?: string | unknown) => void } | null>(null);
export function CalculatorProvider({ children }: { children: ReactNode }) {
  const [open, setOpen] = useState(false);
  const [activeTab, setActiveTab] = useState<string>("calculator");
  const openCalculator = (tabOrEvent?: string | unknown) => {
    if (typeof tabOrEvent === "string") {
      setActiveTab(tabOrEvent);
    }
    setOpen(true);
  };
  return <CalculatorContext.Provider value={{ openCalculator }}>
    {children}<CalculatorModal open={open} onClose={() => setOpen(false)} initialTab={activeTab} />
  </CalculatorContext.Provider>;
}
export function useCalculator() {
  const context = useContext(CalculatorContext);
  if (!context) throw new Error("CalculatorProvider is required.");
  return context;
}
