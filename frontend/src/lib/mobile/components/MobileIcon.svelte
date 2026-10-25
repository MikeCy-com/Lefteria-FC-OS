<script lang="ts">
  import type { MobileIconName } from './component-types.ts';

  let { name, size = 24 }: { name: MobileIconName; size?: number } = $props();
  const paths: Record<MobileIconName, readonly string[]> = {
    home: ['M3 10.5 12 3l9 7.5', 'M5 9v11h5v-6h4v6h5V9'],
    calendar: ['M5 5h14a2 2 0 0 1 2 2v12a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V7a2 2 0 0 1 2-2Z', 'M7 3v4m10-4v4M3 11h18M7 15h1m3 0h1m3 0h1m-9 3h1m3 0h1'],
    users: ['M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2', 'M17 4a4 4 0 0 1 0 8m5 9v-2a4 4 0 0 0-3-3.9', 'M13 7a4 4 0 1 1-8 0 4 4 0 0 1 8 0Z'],
    messages: ['M21 11a8.5 8.5 0 0 1-8.5 8.5H6L2 22l1.5-5.5A8.5 8.5 0 1 1 21 11Z', 'M7 10h10M7 14h6'],
    menu: ['M4 6h16M4 12h16M4 18h16'],
    bell: ['M18 8a6 6 0 0 0-12 0c0 7-3 7-3 9h18c0-2-3-2-3-9', 'M10 21h4'],
    user: ['M20 21v-2a7 7 0 0 0-14 0v2', 'M17 7a4 4 0 1 1-8 0 4 4 0 0 1 8 0Z'],
    plus: ['M12 5v14M5 12h14'],
    chevron: ['m6 9 6 6 6-6'],
    arrow: ['M4 12h16m-6-6 6 6-6 6'],
    phone: ['M7 3 4 4c-2 1 0 7 4 11s10 6 11 4l2-3-5-3-2 2c-3-1-5-3-6-6l2-2-3-4Z'],
    shield: ['m12 3 8 3v6c0 5-8 9-8 9s-8-4-8-9V6l8-3Z', 'm8 12 3 3 5-6'],
    trophy: ['M8 3h8v6a4 4 0 0 1-8 0V3Z', 'M8 5H4v3a4 4 0 0 0 4 4m8-7h4v3a4 4 0 0 1-4 4M12 13v7m-4 1h8'],
    cart: ['M3 3h2l2.5 12h11L21 7H6', 'M10 20a1 1 0 1 1-2 0 1 1 0 0 1 2 0Zm9 0a1 1 0 1 1-2 0 1 1 0 0 1 2 0Z'],
    wallet: ['M20 7H5a2 2 0 0 1 0-4h13v4m2 0a1 1 0 0 1 1 1v12H5a2 2 0 0 1-2-2V5', 'M21 11h-6v5h6m-3-2.5h.01'],
    grid: ['M3 3h7v7H3V3Zm11 0h7v7h-7V3ZM3 14h7v7H3v-7Zm11 0h7v7h-7v-7Z'],
    back: ['M20 12H4m6-6-6 6 6 6'],
    check: ['m5 12 4 4L19 6']
  };
</script>

<svg width={size} height={size} viewBox="0 0 24 24" fill="none" stroke="currentColor"
  stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false">
  {#each paths[name] as d}<path {d} />{/each}
</svg>
