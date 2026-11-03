<script lang="ts">
  import PublicCatalogList from '../../../lib/public/PublicCatalogList.svelte';
  import type { CatalogData } from '../../../lib/server/public-catalog';
  let { data }: { data: CatalogData } = $props();
</script>
<PublicCatalogList {data} />
