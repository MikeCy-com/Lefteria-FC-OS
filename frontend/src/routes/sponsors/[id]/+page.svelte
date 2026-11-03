<script lang="ts">
  import PublicCatalogDetail from '../../../lib/public/PublicCatalogDetail.svelte';
  import type { CatalogDetailData } from '../../../lib/server/public-catalog';
  let { data }: { data: CatalogDetailData } = $props();
</script>
<PublicCatalogDetail {data} />
