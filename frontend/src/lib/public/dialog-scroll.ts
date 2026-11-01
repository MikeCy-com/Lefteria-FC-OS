type ScrollLock = { count: number; overflow: string; overflowPriority: string; padding: string; paddingPriority: string };
const locks = new WeakMap<Document, ScrollLock>();

// Native dialog supplies modality/focus, but does not consistently prevent
// background wheel scrolling. Preserve prior inline styles and nested owners.
export function lockPageScroll(document: Document): () => void {
  let state = locks.get(document);
  if (state) ++state.count;
  else {
    const style = document.body.style;
    state = { count: 1, overflow: style.getPropertyValue('overflow'), overflowPriority: style.getPropertyPriority('overflow'),
      padding: style.getPropertyValue('padding-right'), paddingPriority: style.getPropertyPriority('padding-right') };
    locks.set(document,state);
    const view = document.defaultView;
    const gap = Math.max(0,(view?.innerWidth ?? document.documentElement.clientWidth)-document.documentElement.clientWidth);
    const padding = Number.parseFloat(view?.getComputedStyle(document.body).paddingRight ?? '0') || 0;
    style.setProperty('overflow','hidden','important');
    if (gap) style.setProperty('padding-right',padding+gap+'px','important');
  }
  let released = false;
  return () => {
    if (released) return; released = true;
    if (!state || --state.count !== 0) return;
    const style = document.body.style;
    if (state.overflow) style.setProperty('overflow',state.overflow,state.overflowPriority); else style.removeProperty('overflow');
    if (state.padding) style.setProperty('padding-right',state.padding,state.paddingPriority); else style.removeProperty('padding-right');
    locks.delete(document);
  };
}
