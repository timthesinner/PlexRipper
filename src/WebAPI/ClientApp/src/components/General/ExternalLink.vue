<template>
	<p-btn :href="href" icon-mode :button-type="externalLinkButtonType" />
</template>

<script lang="ts">
import { Component, Prop, Vue } from 'vue-property-decorator';
import ButtonType from '@enums/buttonType';

@Component
export default class ExternalLink extends Vue {
	@Prop({ required: true, type: String })
	readonly href!: string;

	get externalLinkButtonType(): ButtonType {
		return ButtonType.ExternalLink;
	}
}
</script>
