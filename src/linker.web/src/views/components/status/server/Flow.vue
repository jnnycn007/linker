<template>
    <div class="flow-wrap" v-if="config">
        <p>{{$t('flow.online')}} 
            <a href="javascript:;" @click="flow.map=true" :title="`${$t('flow.thisServer')}\r\n${$t('flow.online')}/${$t('flow.online.7day')}`">{{flow.overallOnline}}</a>
            <a href="javascript:;" @click="flow.allmap=true" :title="`${$t('flow.allServer')}\r\n${$t('flow.online')}/${$t('flow.online.7day')}/${$t('flow.server')}`">{{ flow.serverOnline }}</a>
        </p>
        <p>{{$t('flow.upload')}} <a href="javascript:;"  @click="flow.count = true" :title="`${$t('flow.thisServer')}\r\n${$t('flow.allsend')}`">{{flow.overallSendtSpeed}}</a></p>
        <p>{{$t('flow.download')}} <a href="javascript:;"  @click="flow.count = true" :title="`${$t('flow.thisServer')}\r\n${$t('flow.allrecv')}`">{{flow.overallReceiveSpeed}}</a></p>
    </div>
    <Flow :config="config" :title="$t('status.server')"></Flow>
</template>

<script>
import Flow from '../../flow/Index.vue'
import { provideFlow } from '../../flow/flow';
export default {
    props:['config'],
    components:{Flow},
    setup (props) {
        const {flow} = provideFlow();

        return {
            config:props.config,flow
        }
    }
}
</script>

<style lang="stylus" scoped>
html.dark .flow-wrap{
    background-color: #242526;
    border-color: #575c61;
}
.flow-wrap{
    padding:.4rem;
    font-weight:bold;position:absolute;right:0.5rem;bottom:80%;
    border:1px solid #ddd;
    background-color:#fff;
    z-index :9
    &>a,&>p{
        line-height:normal;
        white-space: nowrap;
        display:block;
    }
}
</style>