import { createContext, useContext, useState } from "react";
import type { ReactNode } from "react";
import { CalculatorModal } from "./CalculatorModal";

const CalculatorContext = createContext<{ openCalculator: () => void } | null>(null);
export function CalculatorProvider({ children }: { children: ReactNode }) {
  const [open, setOpen] = useState(false);
  return <CalculatorContext.Provider value={{ openCalculator: () => setOpen(true) }}>
    {children}<CalculatorModal open={open} onClose={() => setOpen(false)} />
  </CalculatorContext.Provider>;
}
export function useCalculator() {
  const context = useContext(CalculatorContext);
  if (!context) throw new Error("CalculatorProvider is required.");
  return context;
}
