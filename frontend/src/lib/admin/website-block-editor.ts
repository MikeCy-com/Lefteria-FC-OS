import type { WebsiteBlock } from '../public/website-pages.ts';

// Pure editor operations. The database schema remains unchanged: block
// identity and ordering are represented by the JSON array position.
export function moveWebsiteBlock(blocks: readonly WebsiteBlock[], from: number, to: number): WebsiteBlock[] {
  if (!Number.isInteger(from) || !Number.isInteger(to) || from < 0 || to < 0 ||
      from >= blocks.length || to >= blocks.length || from === to) return [...blocks];
  const next = [...blocks];
  const [moved] = next.splice(from, 1);
  next.splice(to, 0, moved);
  return next;
}

export function duplicateWebsiteBlock(blocks: readonly WebsiteBlock[], index: number): WebsiteBlock[] {
  if (!Number.isInteger(index) || index < 0 || index >= blocks.length || blocks.length >= 40)
    return [...blocks];
  const block = blocks[index];
  const duplicate: WebsiteBlock = {
    type:block.type,text:block.text,url:block.url,
    ...(block.layout ? {layout:{...block.layout}} : {}),
    ...(block.type==='heading' && block.level!=null ? {level:block.level} : {})
  };
  return [...blocks.slice(0,index+1), duplicate, ...blocks.slice(index+1)];
}
